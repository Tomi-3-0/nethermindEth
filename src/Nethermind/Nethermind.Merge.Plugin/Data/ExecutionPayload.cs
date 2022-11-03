//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
//
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
//

using System;
using System.Collections.Generic;
using System.Linq;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Int256;
using Nethermind.Serialization.Rlp;
using Nethermind.State.Proofs;
using Newtonsoft.Json;

namespace Nethermind.Merge.Plugin.Data
{
    /// <summary>
    /// A data object representing a block as being sent from the execution layer to the consensus layer.
    ///
    /// <seealso cref="https://github.com/ethereum/execution-apis/blob/main/src/engine/specification.md#executionpayloadv1"/>
    /// </summary>
    public class ExecutionPayload
    {
        // constructor needed for tests
        public ExecutionPayload()
        {
            BlockHash = Keccak.Zero;
            ParentHash = Keccak.Zero;
            FeeRecipient = Address.Zero;
            StateRoot = Keccak.Zero;
            ReceiptsRoot = Keccak.Zero;
            LogsBloom = Bloom.Empty;
            PrevRandao = Keccak.Zero;
            ExtraData = Array.Empty<byte>();
        }

        public ExecutionPayload(Block block)
        {
            BlockHash = block.Hash!;
            ParentHash = block.ParentHash!;
            FeeRecipient = block.Beneficiary!;
            StateRoot = block.StateRoot!;
            BlockNumber = block.Number;
            GasLimit = block.GasLimit;
            GasUsed = block.GasUsed;
            ReceiptsRoot = block.ReceiptsRoot!;
            LogsBloom = block.Bloom!;
            PrevRandao = block.MixHash ?? Keccak.Zero;
            SetTransactions(block.Transactions);
            ExtraData = block.ExtraData!;
            Timestamp = block.Timestamp;
            BaseFeePerGas = block.BaseFeePerGas;
            SetWithdrawals(block.Withdrawals);
        }

        public virtual bool TryGetBlock(out Block? block, UInt256? totalDifficulty = null)
        {
            try
            {
                BlockHeader header = new(ParentHash, Keccak.OfAnEmptySequenceRlp, FeeRecipient, UInt256.Zero, BlockNumber, GasLimit, Timestamp, ExtraData)
                {
                    Hash = BlockHash,
                    ReceiptsRoot = ReceiptsRoot,
                    StateRoot = StateRoot,
                    Bloom = LogsBloom,
                    GasUsed = GasUsed,
                    BaseFeePerGas = BaseFeePerGas,
                    Nonce = 0,
                    MixHash = PrevRandao,
                    Author = FeeRecipient,
                    IsPostMerge = true,
                    TotalDifficulty = totalDifficulty
                };

                var transactions = GetTransactions();
                var withdrawals = DecodedWithdrawals();

                header.TxRoot = new TxTrie(transactions).RootHash;

                if (withdrawals != null)
                    header.WithdrawalsRoot = new WithdrawalTrie(withdrawals).RootHash;

                block = new(header, transactions, Array.Empty<BlockHeader>(), withdrawals);

                return true;
            }
            catch (Exception)
            {
                block = null;

                return false;
            }
        }

        public Keccak ParentHash { get; set; } = Keccak.Zero;
        public Address FeeRecipient { get; set; } = Address.Zero;
        public Keccak StateRoot { get; set; } = Keccak.Zero;
        public Keccak ReceiptsRoot { get; set; } = Keccak.Zero;

        //[JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public Bloom LogsBloom { get; set; } = Bloom.Empty;
        public Keccak PrevRandao { get; set; } = Keccak.Zero;

        //[JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long BlockNumber { get; set; }
        public long GasLimit { get; set; }
        public long GasUsed { get; set; }
        public ulong Timestamp { get; set; }
        public byte[] ExtraData { get; set; } = Array.Empty<byte>();
        public UInt256 BaseFeePerGas { get; set; }

        //[JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public Keccak BlockHash { get; set; } = Keccak.Zero;

        /// <summary>
        /// Array of transaction objects, each object is a byte list (DATA) representing TransactionType || TransactionPayload or LegacyTransaction as defined in EIP-2718
        /// </summary>
        public byte[][] Transactions { get; set; } = Array.Empty<byte[]>();

        /// <summary>
        /// Gets or sets an RLP-encoded collection of <see cref="Withdrawal"/> as defined in
        /// <see href="https://eips.ethereum.org/EIPS/eip-4895">EIP-4895</see>.
        /// </summary>
        public IEnumerable<byte[]>? Withdrawals { get; set; }

        public override string ToString() => $"{BlockNumber} ({BlockHash})";

        public void SetTransactions(params Transaction[] transactions) => Transactions = transactions
            .Select(t => Rlp.Encode(t, RlpBehaviors.SkipTypedWrapping).Bytes)
            .ToArray();

        public Transaction[] GetTransactions() => Transactions
            .Select(t => Rlp.Decode<Transaction>(t, RlpBehaviors.SkipTypedWrapping))
            .ToArray();

        /// <summary>
        /// Decodes the <see cref="Withdrawals"/> and returns a collection of <see cref="Withdrawal"/>.
        /// </summary>
        /// <returns>An RLP-decoded collection of <see cref="Withdrawal"/>.</returns>
        public IEnumerable<Withdrawal>? DecodedWithdrawals() => Withdrawals?
            .Select(w => Rlp.Decode<Withdrawal>(w, RlpBehaviors.None));

        public void SetWithdrawals(params Withdrawal[]? withdrawals)
        {
            if (withdrawals != null)
                Withdrawals = withdrawals
                .Select(t => Rlp.Encode(t).Bytes)
                .ToArray();
        }
    }
}