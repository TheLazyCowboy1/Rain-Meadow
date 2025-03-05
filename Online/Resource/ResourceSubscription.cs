using RainMeadow.Generics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RainMeadow
{
    public class ResourceSubscription : DynamicDeltaOptimizer<OnlineState, OnlineState>, OnlineStateMessage.IStateSource // Feed resource state to player
    {
        public OnlineResource resource;
        public OnlinePlayer player;
        public Queue<OnlineStateMessage> OutgoingStates = new(32);
        public OnlineResource.ResourceState lastAcknoledgedState;
        private int basecooldown;
        private int cooldown;

        public ResourceSubscription(OnlineResource resource, OnlinePlayer player)
        {
            this.resource = resource;
            this.player = player;
            if (resource is Lobby or WorldSession) basecooldown = 5; // 0 for room
            if (!resource.isAvailable) throw new InvalidOperationException("not available");
            if (player.isMe) throw new InvalidOperationException("subscribed to self");
        }

        public void Update(uint tick)
        {
            if (!resource.isAvailable) throw new InvalidOperationException("not available");
            if (!resource.isOwner) throw new InvalidOperationException("not owner");
            if (!resource.isActive) return; // resource not ready yet

            if (EventMath.IsNewerOrEqual(player.latestTickAck, resource.lastModified)) // player has acked latest relevant changes
            {
                if (cooldown > 0) // don't spam
                {
                    cooldown--;
                    //return; //temporarily disabled!
                }
            }

            cooldown = basecooldown;
            cooldown--;

            if (player.recentlyAckdTicks.Count > 0)
            {
                while (OutgoingStates.Count > 0 && EventMath.IsNewer(player.oldestTickToConsider, OutgoingStates.Peek().tick))
                {
                    RainMeadow.Trace("Discarding obsolete:" + OutgoingStates.Peek().tick);
                    OutgoingStates.Dequeue(); // discard obsolete
                }
                while (OutgoingStates.Count > 0 && player.recentlyAckdTicks.Contains(OutgoingStates.Peek().tick))
                {
                    RainMeadow.Trace("Considering candidate:" + OutgoingStates.Peek().tick);
                    //ProcessStateMessage(OutgoingStates.Peek());

                    lastAcknoledgedState = (OnlineResource.ResourceState)OutgoingStates.Dequeue().sourceState; // use most recent available
                }
            }

            var newState = resource.GetState(tick);
            if (lastAcknoledgedState != null)
            {
                RainMeadow.Trace($"sending delta for tick {newState.tick} from reference {lastAcknoledgedState.tick}");
                //var delta = (OnlineResource.ResourceState)newState.Delta(lastAcknoledgedState, player);
                var delta = (OnlineResource.ResourceState)ProcessDelta(newState, lastAcknoledgedState, player);

                //SetDeltas(delta, player);

                //RainMeadow.Trace("Sending delta:\n" + delta.DebugPrint(0));
                OutgoingStates.Enqueue(player.QueueStateMessage(new OnlineStateMessage(delta, newState, this, true, tick, delta.baseline)));
            }
            else
            {
                RainMeadow.Trace($"sending absolute state for tick {newState.tick}");

                //ResetFields(newState);
                ResetSendFrequencies(newState.handler.deltaSendFrequencies);

                //RainMeadow.Trace("Sending full:\n" + newState.DebugPrint(0));
                OutgoingStates.Enqueue(player.QueueStateMessage(new OnlineStateMessage(newState, newState, this, false, tick, 0)));
            }
        }

        public void ResetDeltas()
        {
            RainMeadow.Debug($"delta reset for {resource} -> {player}");
            RainMeadow.Debug($"recent states were [{string.Join(", ", OutgoingStates.Select(s => s.sentAsDelta ? $"{s.tick}d{s.baseline}" : $"{s.tick}"))}]");
            lastAcknoledgedState = null;
            OutgoingStates = new Queue<OnlineStateMessage>(OutgoingStates.Where(x => !x.sentAsDelta && x.tick > player.latestTickAck));
        }

        public override float GetDeltaSendFrequency(OnlineState self, OnlinePlayer? player, OnlinePhysicalObject? opo)
        {
            return self.GetStateSendFrequency(player, opo);
        }
        public override OnlineState ProcessDelta(OnlineState self, OnlineState other, bool[] ignoredDeltas, OnlinePlayer? player)
        {
            return self.Delta(other, ignoredDeltas, player);
        }

        public void Sent(OnlineStateMessage state)
        {
            // no op
        }

        public void Failed(OnlineStateMessage state)
        {
            OutgoingStates = new(OutgoingStates.Where(e => e.tick != state.tick));
        }
    }
}