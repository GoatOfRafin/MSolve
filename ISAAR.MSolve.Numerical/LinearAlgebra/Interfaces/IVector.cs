﻿using System;

namespace ISAAR.MSolve.Numerical.LinearAlgebra.Interfaces
{
    public interface IVector
    {
        int Length { get; }
        double Norm { get; }
        double this[int x] { get; set; }
        void Multiply(double coefficient);
        void CopyTo(Array array, int index);
        void Clear();
    }
}
