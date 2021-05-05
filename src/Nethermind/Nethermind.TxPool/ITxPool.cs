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

using System;
using System.Collections.Generic;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Int256;

namespace Nethermind.TxPool
{
    public interface ITxPool 
    {
        int GetPendingTransactionsCount();
        Transaction[] GetPendingTransactions();
        Transaction[] GetOwnPendingTransactions();
        
        /// <summary>
        /// Grouped by sender address, sorted by nonce and later tx pool sorting
        /// </summary>
        /// <returns></returns>
        IDictionary<Address, Transaction[]> GetPendingTransactionsBySender();
        void AddPeer(ITxPoolPeer peer);
        void RemovePeer(PublicKey nodeId);
        AddTxResult AddTransaction(Transaction tx, TxHandlingOptions handlingOptions);
        bool RemoveTransaction(Transaction tx, bool removeBelowThisTxNonce = false);
        void RemoveOrUpdateBucket(Address? senderAddress);
        bool IsInHashCache(Keccak? hash);
        bool TryGetPendingTransaction(Keccak hash, out Transaction? transaction);
        UInt256 ReserveOwnTransactionNonce(Address address);
        event EventHandler<TxEventArgs> NewDiscovered;
        event EventHandler<TxEventArgs> NewPending;
        event EventHandler<TxEventArgs> RemovedPending;
        
        public uint FutureNonceRetention { get; }
        long? BlockGasLimit { get; set; }
    }
}
