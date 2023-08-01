// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Microsoft.EntityFrameworkCore.Design;

namespace Nethermind.Core.Collections;

public readonly struct EliasFanoS
{
    public readonly DArray HighBits;
    public readonly BitVector LowBits;
    public readonly int LowLen;
    public readonly UIntPtr Universe;

    public EliasFanoS(DArray highBits, BitVector lowBits, int lowLen, UIntPtr universe)
    {
        HighBits = highBits;
        LowBits = lowBits;
        LowLen = lowLen;
        Universe = universe;
    }

    public int Rank(UIntPtr pos)
    {
        if (Universe < pos) throw new ArgumentException();
        if (Universe == pos) return HighBits.IndexS1.NumPositions;

        int hRank = (int)(pos >> LowLen);
        int hPos = HighBits.Select0(hRank)!.Value;
        int rank = hPos - hRank;

        UIntPtr lPos = pos & (((UIntPtr)1 << LowLen) - 1);

        while ((hPos > 0)
               && HighBits.Data.GetBit(hPos-1)!.Value
               && (LowBits.GetBits((rank-1)*LowLen, LowLen) >= lPos))
        {
            rank -= 1;
            hPos -= 1;
        }

        return rank;
    }
}

public struct EliasFano
{
    public BitVector HighBits;
    public BitVector LowBits;
    public UIntPtr Universe;
    public int _numValues;
    public int Pos;
    public UIntPtr Last;
    public int LowLen;

    public EliasFano(UIntPtr universe, int numValues)
    {
        int lowLen = (int)Math.Ceiling(Math.Log2(universe / (UIntPtr)numValues));
        HighBits = new BitVector((numValues + 1) + (int)(universe >> lowLen) + 1);
        LowBits = new BitVector();
        Universe = universe;
        _numValues = numValues;
        Pos = 0;
        Last = 0;
        LowLen = lowLen;
    }


    public void Push(UIntPtr val)
    {
        if (val < Last) throw new OperationException("not allowed");
        if (Universe < Last) throw new OperationException("not allowed");
        if (_numValues <= Pos) throw new OperationException("not allowed");

        Last = val;
        UIntPtr lowMask = (((UIntPtr)1) << LowLen) - 1;

        if (LowLen != 0)
        {
            LowBits.PushBits(val & lowMask, LowLen);
        }
        HighBits.SetBit((int)(val >> LowLen) + Pos, true);
        Pos += 1;
    }

    public EliasFanoS Build()
    {
        return new EliasFanoS(new DArray(HighBits), LowBits, LowLen, Universe);
    }
}
