using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using AsyncIO;
using Libplanet;
using Libplanet.Action;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Blocks;
using Libplanet.Crypto;
using Libplanet.Net;
using Libplanet.Store;
using Libplanet.Tx;
using Nekoyume.Action;
#if BLOCK_LOG_USE
using Nekoyume.Helper;
#endif
using Nekoyume.Serilog;
using Serilog;
using UnityEngine;

namespace Nekoyume
{
    /// <summary>
    /// 메인넷에 직접 붙어서 블록을 마이닝 한다.
    /// </summary>
    public class Agent : IDisposable
    {
        private class DebugPolicy : IBlockPolicy<PolymorphicAction<ActionBase>>
        {
            public InvalidBlockException ValidateNextBlock(IReadOnlyList<Block<PolymorphicAction<ActionBase>>> blocks, Block<PolymorphicAction<ActionBase>> nextBlock)
            {
                return null;
            }

            public long GetNextBlockDifficulty(IReadOnlyList<Block<PolymorphicAction<ActionBase>>> blocks)
            {
                Thread.Sleep(SleepInterval);
                return blocks.Any() ? 1 : 0;
            }
        }
        
        private const float TxProcessInterval = 3.0f;
        private const float ActionRetryInterval = 15.0f;
        private const int RewardAmount = 1;
        private const int SwarmDialTimeout = 5000;
        
        private static readonly TimeSpan BlockInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan SleepInterval = TimeSpan.FromSeconds(3);
        
        private readonly ConcurrentQueue<PolymorphicAction<ActionBase>> _queuedActions = new ConcurrentQueue<PolymorphicAction<ActionBase>>();
        private readonly ConcurrentQueue<GameAction> _actionPool = new ConcurrentQueue<GameAction>();
        private readonly BlockChain<PolymorphicAction<ActionBase>> _blocks;
        private readonly Swarm _swarm;
        
        private readonly PrivateKey _agentPrivateKey;
        
        public Guid ChainId => _blocks.Id;
        
        public event EventHandler PreloadStarted;
        public event EventHandler<BlockDownloadState> PreloadProcessed;
        public event EventHandler PreloadEnded;

        static Agent() 
        {
            ForceDotNet.Force();
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Sink(new UnityDebugSink())
                .CreateLogger();
        }

        public Agent(
            PrivateKey agentPrivateKey,
            string path,
            Guid chainId,
            IEnumerable<Peer> peers,
            IEnumerable<IceServer> iceServers,
            string host,
            int? port)
        {
            var policy = GetPolicy();
            _agentPrivateKey = agentPrivateKey;
            _blocks = new BlockChain<PolymorphicAction<ActionBase>>(
                policy,
                new FileStore(path),
                chainId);
#if BLOCK_LOG_USE
            FileHelper.WriteAllText("Block.log", "");
#endif

            _swarm = new Swarm(
                agentPrivateKey,
                appProtocolVersion: 1,
                millisecondsDialTimeout: SwarmDialTimeout,
                host: host,
                listenPort: port,
                iceServers: iceServers);

            foreach (var peer in peers)
            {
                if (peer.PublicKey != agentPrivateKey.PublicKey)
                {
                    _swarm.Add(peer);
                }
            }

            AddressBook.Agent.Value = _agentPrivateKey.PublicKey.ToAddress();
        }

        public void Dispose()
        {
            _swarm?.StopAsync().Wait(0);
        }
        
        public IEnumerator CoSwarmRunner()
        {
            PreloadStarted?.Invoke(this, null);
            
            // Unity 플레이어에서 성능 문제로 Async를 직접 쓰지 않고 
            // Task.Run(async ()) 로 감쌉니다.
            var swarmPreloadTask = Task.Run(async () =>
            {
                await _swarm.PreloadAsync(_blocks,
                    new Progress<BlockDownloadState>(state => PreloadProcessed?.Invoke(this, state)));
            });
            yield return new WaitUntil(() => swarmPreloadTask.IsCompleted);

            PreloadEnded?.Invoke(this, null);

            var swarmStartTask = Task.Run(async () => await _swarm.StartAsync(_blocks));
            yield return new WaitUntil(() => swarmStartTask.IsCompleted);
        }

        public IEnumerator CoActionRetryor() 
        {
            HashDigest<SHA256>? previousTipHash = _blocks.Tip?.Hash;
            while (true)
            {
                yield return new WaitForSeconds(ActionRetryInterval);

                if (_blocks.Tip is null ||
                    _blocks.Tip.Hash is HashDigest<SHA256> currentTipHash &&
                    currentTipHash.Equals(previousTipHash))
                {
                    continue;
                }

                previousTipHash = _blocks.Tip.Hash;
                var task = Task.Run(() =>
                {
                    return (HashSet<Guid>)_blocks.GetStates(
                        new[] { GameAction.ProcessedActionsAddress }
                    ).GetValueOrDefault(
                        GameAction.ProcessedActionsAddress,
                        new HashSet<Guid>()
                    );
                });
                
                yield return new WaitUntil(() => task.IsCompleted);

                if (!task.IsFaulted && !task.IsCanceled) 
                {
                    var processedActions = task.Result;
                    while (_actionPool.TryDequeue(out GameAction action)) 
                    {
                        if (!processedActions.Contains(action.Id))
                        {
                            _queuedActions.Enqueue(action);
                        }
                    }
                }
            }
        }

        public IEnumerator CoTxProcessor()
        {
            while (true)
            {
                yield return new WaitForSeconds(TxProcessInterval);
                var actions = new List<PolymorphicAction<ActionBase>>();

                while (_queuedActions.TryDequeue(out PolymorphicAction<ActionBase> action))
                {
                    actions.Add(action);
                    if (action.InnerAction is GameAction asGameAction) 
                    {
                        _actionPool.Enqueue(asGameAction);
                    }
                }

                if (actions.Any())
                {
                    StageActions(actions);
                }
            }
        }

        public IEnumerator CoMiner()
        {
            while (true)
            {
                var tx = Transaction<PolymorphicAction<ActionBase>>.Create(
                        _agentPrivateKey,
                        new List<PolymorphicAction<ActionBase>>()
                        {
                            new RewardGold { gold = RewardAmount }
                        },
                        timestamp: DateTime.UtcNow);
                var txs = new HashSet<Transaction<PolymorphicAction<ActionBase>>> { tx };

                var task = Task.Run(() =>
                {
                    _blocks.StageTransactions(txs);
                    var block = _blocks.MineBlock(AddressBook.Agent.Value);
                    _swarm.BroadcastBlocks(new[] {block});
                    return block;
                });
                yield return new WaitUntil(() => task.IsCompleted);

                if (!task.IsCanceled && !task.IsFaulted)
                {
                    var block = task.Result;
                    Debug.Log($"created block index: {block.Index}, difficulty: {block.Difficulty}");
#if BLOCK_LOG_USE
                    FileHelper.AppendAllText("Block.log", task.Result.ToVerboseString());
#endif
                }
                else
                {
                    if (task.IsFaulted)
                    {
                        Debug.LogException(task.Exception);
                    }
                    _blocks.UnstageTransactions(txs);
                }
            }
        }

        public void EnqueueAction(GameAction gameAction)
        {
            _queuedActions.Enqueue(gameAction);
        }
        
        public object GetState(Address address)
        {
            AddressStateMap states = _blocks.GetStates(new[] {address});
            states.TryGetValue(address, out object value);
            return value;
        }
        
        private void StageActions(IEnumerable<PolymorphicAction<ActionBase>> actions)
        {
            var tx = AvatarManager.MakeTransaction(actions);
            _blocks.StageTransactions(new HashSet<Transaction<PolymorphicAction<ActionBase>>> {tx});
            _swarm.BroadcastTxs(new[] { tx });
        }

        private IBlockPolicy<PolymorphicAction<ActionBase>> GetPolicy()
        {
# if UNITY_EDITOR
            return new DebugPolicy();
# else
            return new BlockPolicy<PolymorphicAction<ActionBase>>(
                BlockInterval,
                0x2000,
                256
            );
#endif
        }
    }
}
