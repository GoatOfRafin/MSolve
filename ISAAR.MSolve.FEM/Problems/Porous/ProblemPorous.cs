﻿using System;
using System.Collections.Generic;
using ISAAR.MSolve.Analyzers.Interfaces;
using ISAAR.MSolve.FEM;
using ISAAR.MSolve.FEM.Entities;
using ISAAR.MSolve.FEM.Interfaces;
using ISAAR.MSolve.FEM.Problems.Porous.Providers;
using ISAAR.MSolve.FEM.Problems.Structural.Providers;
using ISAAR.MSolve.Numerical.LinearAlgebra.Interfaces;
using ISAAR.MSolve.Numerical.LinearAlgebra;

namespace ISAAR.MSolve.Problems
{
    public class ProblemPorous : IImplicitIntegrationProvider, IStaticProvider, INonLinearProvider
    {
        private bool providersInitialized = false;
        private double scalingCoefficient;
        private Dictionary<int, IMatrix2D> ms, cs, ks;
        private Dictionary<int, Sparse2D> qs;
        private readonly Model model;
        private ElementPoreStiffnessProvider stiffnessProvider;
        private ElementPoreDampingProvider dampingProvider;
        private ElementPoreMassProvider massProvider;

        public ProblemPorous(Model model)
        {
            this.model = model;
        }

        private IDictionary<int, IMatrix2D> Ms
        {
            get
            {
                if (ms == null) BuildMs();
                return ms;
            }
        }

        private IDictionary<int, IMatrix2D> Cs
        {
            get
            {
                if (cs == null) BuildCs();
                return cs;
            }
        }

        private IDictionary<int, IMatrix2D> Ks
        {
            get
            {
                if (ks == null)
                    BuildKs();
                else
                    RebuildKs();
                return ks;
            }
        }

        private void InitializeProvidersAndBuildQs(IImplicitIntegrationCoefficients c)
        {
            //if (providersInitialized) return;

            //providersInitialized = true;
            massProvider = new ElementPoreMassProvider(new ElementStructuralMassProvider(), c.Damping);
            dampingProvider = new ElementPoreDampingProvider(new ElementStructuralDampingProvider(), c.Damping);
            stiffnessProvider = new ElementPoreStiffnessProvider(new ElementStructuralStiffnessProvider(), c.Damping);
            BuildQs();
        }

        private void BuildKs()
        {
            ks = new Dictionary<int, IMatrix2D>(model.SubdomainsDictionary.Count);
            foreach (Subdomain subdomain in model.SubdomainsDictionary.Values)
                ks.Add(subdomain.ID, GlobalMatrixAssemblerSkyline.CalculateGlobalMatrix(subdomain, stiffnessProvider));
        }

        private void RebuildKs()
        {
            foreach (Subdomain subdomain in model.SubdomainsDictionary.Values)
            {
                if (subdomain.MaterialsModified)
                    ks[subdomain.ID] = GlobalMatrixAssemblerSkyline.CalculateGlobalMatrix(subdomain, stiffnessProvider);
                subdomain.ResetMaterialsModifiedProperty();
            }
        }

        private void BuildMs()
        {
            ms = new Dictionary<int, IMatrix2D>(model.SubdomainsDictionary.Count);
            foreach (Subdomain subdomain in model.SubdomainsDictionary.Values)
                ms.Add(subdomain.ID, GlobalMatrixAssemblerSkyline.CalculateGlobalMatrix(subdomain, massProvider));
        }

        private void BuildCs()
        {
            cs = new Dictionary<int, IMatrix2D>(model.SubdomainsDictionary.Count);
            foreach (Subdomain subdomain in model.SubdomainsDictionary.Values)
                cs.Add(subdomain.ID, GlobalMatrixAssemblerSkyline.CalculateGlobalMatrix(subdomain, dampingProvider));
        }

        private void BuildQs()
        {
            qs = new Dictionary<int, Sparse2D>(model.SubdomainsDictionary.Count);
            foreach (Subdomain subdomain in model.SubdomainsDictionary.Values)
                qs.Add(subdomain.ID, BuildQFromSubdomain(subdomain));
        }

        private Sparse2D BuildQFromSubdomain(Subdomain subdomain)
        {
            Sparse2D qSubdomain = new Sparse2D(subdomain.TotalDOFs, subdomain.TotalDOFs);
            foreach (Element element in subdomain.ElementsDictionary.Values)
            {
                if (!(element.ElementType is IPorousFiniteElement)) continue;

                IPorousFiniteElement e = (IPorousFiniteElement)element.ElementType;
                IMatrix2D q = e.CouplingMatrix(element);

                int iElementMatrixRow = 0;
                for (int i = 0; i < element.ElementType.DOFEnumerator.GetDOFTypes(element).Count; i++)
                {
                    Node nodeRow = element.Nodes[i];
                    foreach (DOFType dofTypeRow in element.ElementType.DOFEnumerator.GetDOFTypes(element)[i])
                    {
                        if (dofTypeRow != DOFType.Pore) continue;

                        int dofRow = subdomain.NodalDOFsDictionary[nodeRow.ID][dofTypeRow];
                        if (dofRow != -1)
                        {
                            int iElementMatrixColumn = 0;
                            for (int j = 0; j < element.ElementType.DOFEnumerator.GetDOFTypes(element).Count; j++)
                            {
                                Node nodeColumn = element.Nodes[j];
                                foreach (DOFType dofTypeColumn in element.ElementType.DOFEnumerator.GetDOFTypes(element)[j])
                                {
                                    if (dofTypeColumn == DOFType.Pore) continue;

                                    int dofColumn = subdomain.NodalDOFsDictionary[nodeColumn.ID][dofTypeColumn];
                                    if (dofColumn != -1)
                                    {
                                        qSubdomain[dofColumn, dofRow] += q[iElementMatrixRow, iElementMatrixColumn];
                                    }
                                    iElementMatrixColumn++;
                                }
                            }
                        }
                        iElementMatrixRow++;
                    }
                }
            }

            return qSubdomain;
        }

        private void ScaleSubdomainSolidVector(int id, IVector vector)
        {
            foreach (int nodeID in model.SubdomainsDictionary[id].NodalDOFsDictionary.Keys)
                foreach (DOFType dofType in model.SubdomainsDictionary[id].NodalDOFsDictionary[nodeID].Keys)
                    if (dofType != DOFType.Pore)
                    {
                        int dof = model.SubdomainsDictionary[id].NodalDOFsDictionary[nodeID][dofType];
                        if (dof > -1) vector[dof] *= this.scalingCoefficient;
                    }
        }

        private IMatrix2D CalculateEffectiveMatrixInternal(int id, IImplicitIntegrationCoefficients coefficients)
        {
            //subdomain.Matrix = this.Ks[subdomain.ID];
            //var m = subdomain.Matrix as ILinearlyCombinable;
            //m.LinearCombination(
            //    new double[] 
            //    {
            //        coefficients.Stiffness, coefficients.Mass, coefficients.Damping
            //    },
            //    new IMatrix2D[] 
            //    { 
            //        this.Ks[subdomain.ID], this.Ms[subdomain.ID], this.Cs[subdomain.ID] 
            //    });
            var matrix = this.Ks[id];
            var m = matrix as ILinearlyCombinable;
            m.LinearCombination(
                new double[]
                {
                    coefficients.Stiffness, coefficients.Mass, coefficients.Damping
                },
                new IMatrix2D[]
                {
                    this.Ks[id], this.Ms[id], this.Cs[id]
                });

            return matrix;
        }

        #region IAnalyzerProvider Members
        public void Reset()
        {
            foreach (Subdomain subdomain in model.SubdomainsDictionary.Values)
                foreach (var element in subdomain.ElementsDictionary.Values)
                    element.ElementType.ClearMaterialState();

            cs = null;
            ks = null;
            ms = null;
        }
        #endregion

        #region IImplicitIntegrationProvider Members

        public IMatrix2D CalculateEffectiveMatrix(int id, IImplicitIntegrationCoefficients coefficients)
        {
            InitializeProvidersAndBuildQs(coefficients);
            scalingCoefficient = coefficients.Damping;
            return CalculateEffectiveMatrixInternal(id, coefficients);
        }

        public void ProcessRHS(int id, IImplicitIntegrationCoefficients coefficients)
        {
            // TODO: This fails
            //scalingCoefficient = coefficients.Damping;
            //ScaleSubdomainSolidVector(subdomain, subdomain.RHS);
        }

        public void GetRHSFromHistoryLoad(int timeStep)
        {
            foreach (Subdomain subdomain in model.SubdomainsDictionary.Values)
                for (int i = 0; i < subdomain.Forces.Length; i++)
                    subdomain.Forces[i] = 0;

            model.AssignLoads();
            model.AssignMassAccelerationHistoryLoads(timeStep);
        }

        public void MassMatrixVectorProduct(int id, IVector vIn, double[] vOut)
        {
            this.Ms[id].Multiply(vIn, vOut);
        }

        public void DampingMatrixVectorProduct(int id, IVector vIn, double[] vOut)
        {
            this.Cs[id].Multiply(vIn, vOut);
            double[] vQ = new double[vOut.Length];
            qs[id].Multiply(vIn, vQ);
            for (int i = 0; i < vOut.Length; i++) vOut[i] -= vQ[i];
        }

        #endregion

        #region IStaticProvider Members

        public IMatrix2D CalculateMatrix(int id)
        {
            throw new NotImplementedException();
        }

        //public void CalculateMatrices()
        //{
        //    throw new NotImplementedException();
        //}

        #endregion

        #region INonLinearProvider Members

        public double RHSNorm(double[] rhs)
        {
            //return (new Vector<double>(rhs)).Norm;

            double norm = 0;
            foreach (int nodeID in model.NodalDOFsDictionary.Keys)
                foreach (DOFType dofType in model.NodalDOFsDictionary[nodeID].Keys)
                    if (dofType != DOFType.Pore)
                    {
                        int dof = model.NodalDOFsDictionary[nodeID][dofType];
                        if (dof > -1) norm += rhs[dof] * rhs[dof];
                    }
            return Math.Sqrt(norm);
        }

        public void ProcessInternalRHS(int id, double[] rhs, double[] solution)
        {
            //ScaleSubdomainSolidVector(subdomain, new Vector<double>(rhs));
            //return;

            double[] vQ = new double[rhs.Length];
            qs[id].Multiply(new Vector(solution), vQ);
            for (int i = 0; i < rhs.Length; i++) rhs[i] += vQ[i];
            ScaleSubdomainSolidVector(id, new Vector(rhs));
        }

        #endregion
    }
}
