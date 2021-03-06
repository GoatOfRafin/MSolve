﻿using ISAAR.MSolve.Numerical.LinearAlgebra.Interfaces;

namespace ISAAR.MSolve.Analyzers.Interfaces
{
    public interface IStaticProvider : IAnalyzerProvider
    {
        // TODO: ID fields should be removed and the whole domain decomposition thing must be decoupled from the analyzers
        IMatrix2D CalculateMatrix(int id);
    }
}
