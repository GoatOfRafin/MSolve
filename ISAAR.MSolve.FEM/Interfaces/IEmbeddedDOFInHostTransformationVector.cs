﻿using System.Collections.Generic;
using ISAAR.MSolve.FEM.Entities;
using ISAAR.MSolve.FEM.Embedding;

namespace ISAAR.MSolve.FEM.Interfaces
{
    public interface IEmbeddedDOFInHostTransformationVector
    {
        IList<DOFType> GetDependentDOFTypes { get; }
        IList<IList<DOFType>> GetDOFTypesOfHost(EmbeddedNode node);
        double[][] GetTransformationVector(EmbeddedNode node);
    }
}
