using RainMeadow.Generics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RainMeadow
{
    public class EntityFeed : DynamicDeltaOptimizer<OnlineState, OnlineState>, OnlineStateMessage.IStateSource // Feed entity state to resource owner
    {
        public OnlineResource resource;
        public OnlineEntity entity;
        public OnlinePlayer player;
        public Queue<OnlineStateMessage> OutgoingStates = new(32);
        public OnlineEntity.EntityState lastAcknoledgedState;

        public EntityFeed(OnlineResource resource, OnlineEntity oe)
        {
            this.resource = resource;
            this.player = resource.owner;
            this.entity = oe;
            if (resource.isOwner) throw new InvalidOperationException("feeding myself");
        }

        public void Update(uint tick)
        {
            if (!resource.isAvailable) return; // unloading
            if (resource.isOwner)
            {
                RainMeadow.Error($"Self-feeding entity {entity} for resource {resource}");
                return;
            }
            if (resource.owner != player) // they don't know
            {
                OutgoingStates.Clear();
                lastAcknoledgedState = null;
                player = resource.owner;
            }
            if (player == null) return; // resource owner might be null while unloading

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

                    lastAcknoledgedState = (OnlineEntity.EntityState)OutgoingStates.Dequeue().sourceState; // use most recent available
                }
            }

            var newState = entity.GetState(tick, resource);
            if (lastAcknoledgedState != null)
            {
                RainMeadow.Trace($"sending delta for tick {newState.tick} from reference {lastAcknoledgedState.tick}");
                //var delta = (OnlineEntity.EntityState)newState.Delta(lastAcknoledgedState, player);
                var delta = (OnlineEntity.EntityState)ProcessDelta(newState, lastAcknoledgedState, player);

                //SetDeltas(delta, player);
                //RainMeadow.Trace("Sending delta:\n" + delta.DebugPrint(0));
                OutgoingStates.Enqueue(player.QueueStateMessage(new OnlineStateMessage(new EntityFeedState(delta, resource), newState, this, true, tick, delta.baseline)));
            }
            else
            {
                RainMeadow.Trace($"sending absolute state for tick {newState.tick}");
                //ResetFields(newState);
                ResetSendFrequencies(newState.handler.deltaSendFrequencies);
                //RainMeadow.Trace("Sending full:\n" + newState.DebugPrint(0));
                OutgoingStates.Enqueue(player.QueueStateMessage(new OnlineStateMessage(new EntityFeedState(newState, resource), newState, this, false, tick, 0)));
            }
        }

        public void ResetDeltas()
        {
            RainMeadow.Debug($"delta reset for {entity} in {resource} -> {player}");
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

        public void Sent(OnlineStateMessage stateMessage)
        {
            // no op
        }

        public void Failed(OnlineStateMessage stateMessage)
        {
            OutgoingStates = new(OutgoingStates.Where(e => e.tick != stateMessage.tick));
        }
    }
}