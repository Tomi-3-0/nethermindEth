// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Synchronization;
using Nethermind.Core.Test.Builders;
using Nethermind.Stats;
using Nethermind.Stats.Model;
using Nethermind.Synchronization.Peers;
using Nethermind.Synchronization.Peers.AllocationStrategies;
using Nethermind.Synchronization.StateSync;
using NSubstitute;
using NUnit.Framework;

namespace Nethermind.Synchronization.Test.FastSync;

public class StateSyncAllocationStrategyTests
{
    private static IPeerAllocationStrategy _strategy = new StateSyncAllocationStrategyFactory.AllocationStrategy(new NoopAllocationStrategy());

    [Test]
    public void Can_allocate_node_with_snap()
    {
        Node node = new(TestItem.PublicKeyA, new IPEndPoint(0, 0));
        node.AgreedCapability["eth"] = 67;
        IsNodeAllocated(node, snapProtocolHandler: new object()).Should().BeTrue();
    }

    [Test]
    public void Can_allocate_pre_eth67_node()
    {
        Node node = new(TestItem.PublicKeyA, new IPEndPoint(0, 0));
        node.AgreedCapability["eth"] = 66;
        IsNodeAllocated(node).Should().BeTrue();
    }

    [Test]
    public void Can_allocated_nethermind_eth_67_no_snap()
    {
        Node node = new(TestItem.PublicKeyA, new IPEndPoint(0, 0));
        node.AgreedCapability["eth"] = 67;
        node.ClientId = NodeClientType.Nethermind.ToString();
        IsNodeAllocated(node).Should().BeTrue();
    }

    [Test]
    public void Cannot_allocated_eth67_with_no_snap()
    {
        Node node = new(TestItem.PublicKeyA, new IPEndPoint(0, 0));
        node.AgreedCapability["eth"] = 67;
        IsNodeAllocated(node).Should().BeFalse();
    }

    private bool IsNodeAllocated(Node node, object? snapProtocolHandler = null)
    {
        ISyncPeer syncPeer = Substitute.For<ISyncPeer>();
        syncPeer.Node.Returns(node);
        syncPeer.TryGetSatelliteProtocol("snap", out Arg.Any<ISnapSyncPeer>()).Returns(
            x =>
            {
                x[1] = snapProtocolHandler;
                return snapProtocolHandler != null;
            });
        PeerInfo peerInfo = new PeerInfo(syncPeer);

        return _strategy.Allocate(null, new List<PeerInfo>() { peerInfo }, Substitute.For<INodeStatsManager>(),
            Substitute.For<IBlockTree>()) == peerInfo;
    }

    private class NoopAllocationStrategy : IPeerAllocationStrategy
    {
        public bool CanBeReplaced => false;
        public PeerInfo? Allocate(PeerInfo? currentPeer, IEnumerable<PeerInfo> peers, INodeStatsManager nodeStatsManager, IBlockTree blockTree)
        {
            return peers.FirstOrDefault();
        }
    }
}
