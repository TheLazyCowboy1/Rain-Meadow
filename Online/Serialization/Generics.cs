﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RainMeadow.Generics
{
    // Welcome to generics hell

    /// <summary>
    /// Object that tracks/serializes whether it's a delta
    /// by convention returns object with IsEmptyDelta set on same-value delta (ease for polymorphism)
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IPrimaryDelta<T> : IDelta<T>
    {
        public bool IsEmptyDelta { get; }
        //public T Delta(T other);
        //public T ApplyDelta(T other);
    }

    /// <summary>
    /// Simple delta, doesn't know/serialize whether its delta or not
    /// by convention retuns null on same-value delta
    /// no convenient inheritance/poly support
    /// use new() to instantiate new
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IDelta<T>
    {
        public T Delta(T other);
        public T Delta(T other, OnlinePlayer? player);
        public T ApplyDelta(T other);
    }

    /// <summary>
    /// ID for matching elements in list-wise deltas
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IIdentifiable<T> where T : IEquatable<T>
    {
        public T ID { get; }
    }


    public abstract class DeltaOptimizer<T, V> where T : IDelta<V>
    {
        public const float TEMPORARY_SEND_FREQUENCY = 0.5f;

        public abstract T ProcessDelta(T self, T other, OnlinePlayer? player);

        public virtual float GetDeltaSendFrequency(T self, OnlinePlayer? player, OnlinePhysicalObject? opo) => 1f;

        /// <summary>
        /// Used to if the subdelta is also a DeltaOptimizer, and thus needs to process the player.
        /// Automatically handles both DeltaOptimizers and normal IDeltas
        /// </summary>
        public static U GetOptimizedDelta<U>(IDelta<U> delta, U baseline, OnlinePlayer? player, bool ignoreDeltas = false)
        {
            if (ignoreDeltas) return baseline;
            if (delta.GetType() == baseline.GetType() && delta is DeltaOptimizer<IDelta<U>, U> optDelta) //if same type AND deltaOptimizer
                return (U)optDelta.ProcessDelta(delta, (IDelta<U>)baseline, player);  //include the player
            return delta.Delta(baseline);
        }
    }

    /// <summary>
    /// All fields have the exact same frequency
    /// (the "always" attribute may differ; that is handled in OnlineState.cs)
    /// </summary>
    public abstract class InfrequentDeltaSender<T, V> : DeltaOptimizer<T, V> where T : IDelta<V> where V : class
    {
        private float sendCounter = 1f;
        public float deltaFrequency = 1f;

        public override T ProcessDelta(T self, T other, OnlinePlayer? player)
        {
            return GetIgnoreDeltas(self, player) ? other //send other (baseline) if delta is ignored
                : (GetOptimizedDelta<V>(self, other as V, player) is T output ? output : self); //send self if it somehow fails
        }

        public virtual bool GetIgnoreDeltas(T self, OnlinePlayer? player)
        {
            //try to find player
            OnlinePhysicalObject? opo = null;
            try
            {
                opo = (OnlinePhysicalObject)OnlineManager.lobby.playerAvatars.Find(avatar => avatar.Key.inLobbyId == player?.inLobbyId)
                    .Value.FindEntity(true);
            }
            catch { }
            float opt = GetDeltaSendFrequency(self, player, opo);

            bool ret = false;
            sendCounter -= deltaFrequency * opt * TEMPORARY_SEND_FREQUENCY;
            if (sendCounter > 0)
                ret = true; //don't send; counter hasn't ticked down yet
            else
            {
                sendCounter += 1;
                if (sendCounter < 0) sendCounter = 0; //don't let it run away forever!
            }

            return ret;
        }
    }

    /// <summary>
    /// Field frequencies may vary, thus updating some fields more often than others
    /// </summary>
    public abstract class DynamicDeltaOptimizer<T, V> : DeltaOptimizer<T, V> where T : IDelta<V> where V : class
    {
        private float[] deltaCounters = [];
        private float[] deltaSendFrequencies = [];

        public void ResetSendFrequencies(float[] sendFrequencies)
        {
            deltaSendFrequencies = sendFrequencies;
            deltaCounters = new float[sendFrequencies.Length];
            Utils.FillArray(ref deltaCounters, 1f); //reset all delta counters too
        }

        public abstract T ProcessDelta(T self, T other, bool[] ignoredDeltas, OnlinePlayer? player);

        public override T ProcessDelta(T self, T other, OnlinePlayer? player) => ProcessDelta(self, other, GetIgnoredDeltas(self, player), player);

        public virtual bool[] GetIgnoredDeltas(T self, OnlinePlayer? player)
        {
            //try to find player
            OnlinePhysicalObject? opo = null;
            try
            {
                opo = (OnlinePhysicalObject)OnlineManager.lobby.playerAvatars.Find(avatar => avatar.Key.inLobbyId == player?.inLobbyId)
                    .Value.FindEntity(true);
            }
            catch { }
            float opt = GetDeltaSendFrequency(self, player, opo);

            if (deltaCounters is null || deltaSendFrequencies is null) return [];

            bool[] ret = new bool[deltaCounters.Length];
            for (int i = 0; i < deltaCounters.Length; i++)
            {
                if (deltaSendFrequencies[i] > 0) //ignore fields marked as always (0 == always)
                {
                    deltaCounters[i] -= deltaSendFrequencies[i] * opt * TEMPORARY_SEND_FREQUENCY;
                    if (deltaCounters[i] > 0)
                        ret[i] = true; //don't send; counter hasn't ticked down yet
                    else
                    {
                        deltaCounters[i] += 1;
                        if (deltaCounters[i] < 0) deltaCounters[i] = 0; //don't let it run away forever!
                    }
                }
            }

            return ret;
        }
    }

    public class IdentityComparer<T, U> : IEqualityComparer<T> where T : IIdentifiable<U> where U : IEquatable<U>
    {
        public static IdentityComparer<T, U> instance = new();
        public bool Equals(T x, T y)
        {
            return x.ID.Equals(y.ID);
        }

        public int GetHashCode(T obj)
        {
            return obj.ID.GetHashCode();
        }
    }

    /// <summary>
    /// Dynamic list, order-unaware, no subdelta, no repeated elements
    /// 1 + T * x on fullsend
    /// 1 + T * x' + 1 + T * x" on delta
    /// </summary>
    public abstract class DynamicUnorderedList<T, Imp> : IDelta<Imp>, Serializer.ICustomSerializable where Imp : DynamicUnorderedList<T, Imp>, new()
    {
        public List<T> list;
        public List<T> removed;

        public DynamicUnorderedList() { }
        public DynamicUnorderedList(List<T> list)
        {
            this.list = list;
        }

        public Imp Delta(Imp other, OnlinePlayer? player) => Delta(other);
        public Imp Delta(Imp other)
        {
            if (other == null) { return (Imp)this; }
            Imp delta = new();
            delta.list = list.Except(other.list).ToList();
            delta.removed = other.list.Except(list).ToList();
            return (delta.list.Count == 0 && delta.removed.Count == 0) ? null : delta;
        }

        public Imp ApplyDelta(Imp other)
        {
            Imp result = new();
            result.list = other == null ? list : list.Union(other.list).Except(other.removed).ToList();
            return result;
        }

        public abstract void CustomSerialize(Serializer serializer);
    }

    /// <summary>
    /// Dynamic list, order-aware, no subdelta, no repeated elements
    /// 1 + T * x on fullsend
    /// 1 + (T + 1) * x' + 1 + 1 * x" on delta
    /// </summary>
    public abstract class DynamicOrderedList<T, Imp> : IDelta<Imp>, Serializer.ICustomSerializable where Imp : DynamicOrderedList<T, Imp>, new()
    {
        public List<T> list;
        public List<byte> listIndexes;
        public List<byte> removedIndexes;

        public DynamicOrderedList() { }
        public DynamicOrderedList(List<T> list)
        {
            this.list = list;
        }

        public Imp Delta(Imp other, OnlinePlayer? player) => Delta(other);
        public Imp Delta(Imp other)
        {
            if (other == null) { return (Imp)this; }

            Imp delta = new();
            delta.list = list.Except(other.list).ToList();
            delta.listIndexes = delta.list.Select(e => (byte)list.IndexOf(e)).ToList();
            delta.removedIndexes = other.list.Except(list).Select(e => (byte)other.list.IndexOf(e)).ToList();

            return (delta.list.Count == 0 && delta.removedIndexes.Count == 0) ? null : delta;
        }

        public Imp ApplyDelta(Imp other)
        {
            Imp result = new();
            result.list = list.ToList();
            if (other != null)
            {
                result.list.Capacity = list.Count + other.list.Count;
                for (int j = other.removedIndexes.Count - 1; j >= 0; j--)
                {
                    result.list.RemoveAt(other.removedIndexes[j]);
                }
                for (int i = 0; i < other.list.Count; i++)
                {
                    result.list.Insert(other.listIndexes[i], other.list[i]);
                }
            }
            return result;
        }

        public abstract void CustomSerialize(Serializer serializer);
    }

    /// <summary>
    /// Fixed ordered list, no subdelta
    /// 1 + T * x on fullsend
    /// 1 + (T+1) * x' on delta
    /// </summary>
    public abstract class FixedOrderedList<T, Imp> : IDelta<Imp>, Serializer.ICustomSerializable where Imp : FixedOrderedList<T, Imp>, new()
    {
        public List<T> list;
        public List<byte> updateIndexes;

        public FixedOrderedList() { }
        public FixedOrderedList(List<T> list)
        {
            this.list = list;
        }

        public Imp Delta(Imp other, OnlinePlayer? player) => Delta(other);
        public Imp Delta(Imp other)
        {
            if (other == null) { return (Imp)this; }

            Imp delta = new();
            (delta.list, delta.updateIndexes) = other.list.Select((e, i) => { return (e, i: (byte)i); }).Where(e => list[e.i].Equals(e.e)).ToListTuple();
            return (delta.list.Count == 0) ? null : delta;
        }

        public Imp ApplyDelta(Imp other)
        {
            Imp result = new();
            result.list = list.ToList();
            if (other != null)
            {
                for (int i = 0; i < other.updateIndexes.Count; i++)
                {
                    list[other.updateIndexes[i]] = other.list[i];
                }
            }
            return result;
        }

        public abstract void CustomSerialize(Serializer serializer);
    }

    /// <summary>
    /// Static list, no adds/removes supported, id-elementwise delta
    /// </summary>
    public abstract class FixedIdentifiablesDeltaList<T, U, V, Imp> : InfrequentDeltaSender<Imp, Imp>, IDelta<Imp>, Serializer.ICustomSerializable where T : IDelta<V>, V, IIdentifiable<U> where U : IEquatable<U> where Imp : FixedIdentifiablesDeltaList<T, U, V, Imp>, new()
    {
        public List<T> list;
        public FixedIdentifiablesDeltaList() { }
        public FixedIdentifiablesDeltaList(List<T> list)
        {
            this.list = list;
        }

        public Imp Delta(Imp other, OnlinePlayer? player) => ProcessDelta((Imp)this, other, player);
        public Imp Delta(Imp other) => ProcessDelta((Imp)this, other, null);
        public override Imp ProcessDelta(Imp self, Imp other, OnlinePlayer? player) //here, self == this. Kinda sloppy, but it works
        {
            if (GetIgnoreDeltas(self,player)) return self;
            if (other == null) { return self; }
            Imp delta = new();
            //delta.list = list.Select(sl => (T)sl.Delta(other.list.FirstOrDefault(osl => osl.ID.Equals(sl.ID)))).Where(sl => sl != null).ToList();
            delta.list = list.Select(sl => (T)GetOptimizedDelta(sl, other.list.FirstOrDefault(osl => osl.ID.Equals(sl.ID)), player)).Where(sl => sl != null).ToList();
            return delta.list.Count == 0 ? null : delta;
        }

        public Imp ApplyDelta(Imp other)
        {
            Imp result = new();
            result.list = other == null ? list : list.Select(e => (T)e.ApplyDelta(other.list.FirstOrDefault(o => e.ID.Equals(o.ID)))).ToList();
            return result;
        }

        public abstract void CustomSerialize(Serializer serializer);
    }


    /// <summary>
    /// Dynamic list, id-elementwise comparison, no subdelta
    /// </summary>
    public abstract class DynamicIdentifiablesList<T, U, Imp> : IDelta<Imp>, Serializer.ICustomSerializable where Imp : DynamicIdentifiablesList<T, U, Imp>, new() where T : class, IIdentifiable<U> where U : IEquatable<U>
    {
        public List<T> list;
        public List<U> removed;
        public Dictionary<U, T> lookup;
        private HashSet<U> removedLookup;
        public DynamicIdentifiablesList() { }
        public DynamicIdentifiablesList(List<T> list)
        {
            this.list = list;
            BuildLookup();
        }

        private void BuildLookup()
        {
            this.lookup = list.Select(e => new KeyValuePair<U, T>(e.ID, e)).ToDictionary();
            removedLookup = removed == null ? null : new HashSet<U>(removed);
        }

        public Imp Delta(Imp other, OnlinePlayer? player) => Delta(other);
        public Imp Delta(Imp other)
        {
            if (other == null) { return (Imp)this; }
            Imp delta = new();
            delta.list = list.Select(sl => other.lookup.TryGetValue(sl.ID, out var b) ? (b.Equals(sl) ? null : sl) : sl).Where(sl => sl != null).ToList();
            delta.removed = other.list.Select(e => e.ID).Where(e => !lookup.ContainsKey(e)).ToList();
            delta.BuildLookup();
            return (delta.list.Count == 0 && delta.removed.Count == 0) ? null : delta;
        }

        public Imp ApplyDelta(Imp other)
        {
            Imp result = new();
            if (other == null)
            {
                result.list = list;
            }
            else
            {
                result.list =
                list.Where(e => !other.removedLookup.Contains(e.ID)) // remove
                    .Select(e => other.lookup.TryGetValue(e.ID, out var o) ? o : e) // keep or update
                    .Concat(other.list.Where(o => !lookup.ContainsKey(o.ID))) // add new
                    .ToList();
            }
            result.BuildLookup();
            return result;
        }

        public void CustomSerialize(Serializer serializer)
        {
            SerializeImpl(serializer);
            if (serializer.IsReading)
            {
                BuildLookup();
            }
        }

        public abstract void SerializeImpl(Serializer serializer);
    }

    /// <summary>
    /// Dynamic list, id-elementwise comparison, no subdelta
    /// </summary>
    public abstract class DynamicKVPList<TKey, TValue, Imp> : IDelta<Imp>, Serializer.ICustomSerializable where Imp : DynamicKVPList<TKey, TValue, Imp>, new() where TKey : IEquatable<TKey>
    {
        public List<KeyValuePair<TKey, TValue>> list;
        public List<TKey> removed;
        public Dictionary<TKey, TValue> lookup;
        private HashSet<TKey> removedLookup;
        public DynamicKVPList() { }
        public DynamicKVPList(List<KeyValuePair<TKey, TValue>> list)
        {
            this.list = list;
            BuildLookup();
        }

        private void BuildLookup()
        {
            this.lookup = list.ToDictionary();
            removedLookup = removed == null ? null : new HashSet<TKey>(removed);
        }

        public Imp Delta(Imp other, OnlinePlayer? player) => Delta(other);
        public Imp Delta(Imp baseline)
        {
            if (baseline == null) { return (Imp)this; }
            Imp delta = new();
            delta.list = list.Where(sl=>!baseline.lookup.TryGetValue(sl.Key, out var val) || !val.Equals(sl.Value)).ToList(); // new or changed
            delta.removed = baseline.list.Select(e => e.Key).Where(e => !lookup.ContainsKey(e)).ToList();
            delta.BuildLookup();
            return (delta.list.Count == 0 && delta.removed.Count == 0) ? null : delta;
        }

        public Imp ApplyDelta(Imp baseline)
        {
            Imp result = new();
            if (baseline == null)
            {
                result.list = list;
            }
            else
            {
                result.list =
                list.Where(e => !baseline.removedLookup.Contains(e.Key)) // remove
                    .Select(e => baseline.lookup.TryGetValue(e.Key, out var o) ? new KeyValuePair<TKey, TValue>(e.Key, o) : e) // keep or update
                    .Concat(baseline.list.Where(o => !lookup.ContainsKey(o.Key))) // add new
                    .ToList();
            }
            result.BuildLookup();
            return result;
        }

        public void CustomSerialize(Serializer serializer)
        {
            SerializeImpl(serializer);
            if (serializer.IsReading)
            {
                BuildLookup();
            }
        }

        public abstract void SerializeImpl(Serializer serializer);
    }

    /// <summary>
    /// Dynamic list, id-elementwise delta
    /// </summary>
    public abstract class DynamicIdentifiablesDeltaList<T, U, W, Imp> : InfrequentDeltaSender<Imp, Imp>, IDelta<Imp>, Serializer.ICustomSerializable where T : class, IDelta<W>, W, IIdentifiable<U> where U : IEquatable<U> where Imp : DynamicIdentifiablesDeltaList<T, U, W, Imp>, new()
    {
        public List<T> list;
        public List<U> removed;
        private Dictionary<U, T> lookup;
        private HashSet<U> removedLookup;
        public DynamicIdentifiablesDeltaList() { }
        public DynamicIdentifiablesDeltaList(List<T> list)
        {
            this.list = list;
            BuildLookup();
        }

        private void BuildLookup()
        {
            this.lookup = list.Select(e => new KeyValuePair<U, T>(e.ID, e)).ToDictionary();
            removedLookup = removed == null ? null : new HashSet<U>(removed);
        }

        public Imp Delta(Imp other, OnlinePlayer? player) => ProcessDelta((Imp)this, other, player);
        public Imp Delta(Imp other) => ProcessDelta((Imp)this, other, null); //should never be used, hopefully
        public Imp ProcessDelta(Imp self, Imp other, OnlinePlayer? player)
        {
            if (GetIgnoreDeltas(self,player)) return self;
            if (other == null) { return self; }
            Imp delta = new();
            delta.list = list.Select(sl => other.lookup.TryGetValue(sl.ID, out var b) ? (T)GetOptimizedDelta(sl, b, player) : sl).Where(sl => sl != null).ToList();
            delta.removed = other.list.Select(e => e.ID).Where(e => !lookup.ContainsKey(e)).ToList();
            delta.BuildLookup();
            return (delta.list.Count == 0 && delta.removed.Count == 0) ? null : delta;
        }

        public Imp ApplyDelta(Imp other)
        {
            Imp result = new();
            if (other == null)
            {
                result.list = list;
            }
            else
            {
                result.list =
                list.Where(e => !other.removedLookup.Contains(e.ID)) // remove
                    .Select(e => other.lookup.TryGetValue(e.ID, out var o) ? (T)e.ApplyDelta(o) : e) // keep or update
                    .Concat(other.list.Where(o => !lookup.ContainsKey(o.ID))) // add new
                    .ToList();
            }
            result.BuildLookup();
            return result;
        }

        public void CustomSerialize(Serializer serializer)
        {
            SerializeImpl(serializer);
            if (serializer.IsReading)
            {
                BuildLookup();
            }
        }

        public abstract void SerializeImpl(Serializer serializer);
    }

    /// <summary>
    /// Dynamic list, id-elementwise delta
    /// </summary>
    public abstract class DynamicIdentifiablesPrimaryDeltaList<T, U, W, Imp> : InfrequentDeltaSender<Imp, Imp>, IDelta<Imp>, Serializer.ICustomSerializable where T : class, IPrimaryDelta<W>, W, IIdentifiable<U> where U : IEquatable<U> where Imp : DynamicIdentifiablesPrimaryDeltaList<T, U, W, Imp>, new()
    {
        public List<T> list;
        public List<U> removed;
        public Dictionary<U, T> lookup;
        private HashSet<U> removedLookup;

        public DynamicIdentifiablesPrimaryDeltaList() { }
        public DynamicIdentifiablesPrimaryDeltaList(List<T> list)
        {
            this.list = list;
            BuildLookup();
        }

        private void BuildLookup()
        {
            this.lookup = list.Select(e => new KeyValuePair<U, T>(e.ID, e)).ToDictionary();
            removedLookup = removed == null ? null : new HashSet<U>(removed);
        }

        public Imp Delta(Imp other, OnlinePlayer? player) => ProcessDelta((Imp)this, other, player);
        public Imp Delta(Imp other) => ProcessDelta((Imp)this, other, null); //should never be used, hopefully
        public override Imp ProcessDelta(Imp self, Imp baseline, OnlinePlayer? player)
        {
            if (GetIgnoreDeltas(self,player)) return self;
            if (baseline == null) { return (Imp)this; }
            Imp delta = new();
            //delta.list = list.Select(e => baseline.lookup.TryGetValue(e.ID, out var b) ? (T)e.Delta(b) : e).Where(sl => !sl.IsEmptyDelta).ToList();
            delta.list = list.Select(
                e => baseline.lookup.TryGetValue(e.ID, out var b) ? (T)GetOptimizedDelta(e, b, player) : e).Where(sl => !sl.IsEmptyDelta).ToList();
            delta.removed = baseline.list.Select(e => e.ID).Where(e => !lookup.ContainsKey(e)).ToList();
            delta.BuildLookup();
            return (delta.list.Count == 0 && delta.removed.Count == 0) ? null : delta;
        }

        public Imp ApplyDelta(Imp incoming)
        {
            Imp result = new();
            if (incoming == null)
            {
                result.list = list;
            }
            else
            {
                result.list =
                list.Where(e => !incoming.removedLookup.Contains(e.ID)) // remove
                    .Select(e => incoming.lookup.TryGetValue(e.ID, out var o) ? (T)e.ApplyDelta(o) : e) // keep or update
                    .Concat(incoming.list.Where(o => !lookup.ContainsKey(o.ID))) // add new
                    .ToList();
            }
            result.BuildLookup();
            return result;
        }

        public void CustomSerialize(Serializer serializer)
        {
            SerializeImpl(serializer);
            if (serializer.IsReading)
            {
                BuildLookup();
            }
        }

        public abstract void SerializeImpl(Serializer serializer);
    }

    public class DeltaStates<T, U> : DynamicIdentifiablesPrimaryDeltaList<T, U, OnlineState, DeltaStates<T, U>> where T : OnlineState, IIdentifiable<U> where U : Serializer.ICustomSerializable, IEquatable<U>, new()
    {
        public DeltaStates() : base() { }
        public DeltaStates(List<T> list) : base(list) { }

        public override void SerializeImpl(Serializer serializer)
        {
            serializer.SerializePolyStatesShort(ref list);
            if (serializer.IsDelta) serializer.SerializeShort(ref removed);
        }
    }

    public class DeltaDataStates<T> : DynamicIdentifiablesPrimaryDeltaList<T, byte, OnlineState, DeltaDataStates<T>> where T : OnlineState, IIdentifiable<byte>
    {
        public DeltaDataStates() : base() { }
        public DeltaDataStates(List<T> list) : base(list) { }

        public override void SerializeImpl(Serializer serializer)
        {
            serializer.SerializePolyStatesByte(ref list);
            if (serializer.IsDelta) serializer.Serialize(ref removed);
        }
    }

    public class DynamicIdentifiablesICustomSerializables<T, U> : DynamicIdentifiablesList<T, U, DynamicIdentifiablesICustomSerializables<T, U>> where T : class, IIdentifiable<U>, Serializer.ICustomSerializable, new() where U : IEquatable<U>, Serializer.ICustomSerializable, new()
    {
        public DynamicIdentifiablesICustomSerializables() { }

        public DynamicIdentifiablesICustomSerializables(List<T> list) : base(list) { }

        public override void SerializeImpl(Serializer serializer)
        {
            serializer.SerializeShort(ref list);
            if (serializer.IsDelta) serializer.SerializeShort(ref removed);
        }
    }

    public class DynamicUnorderedUshorts : DynamicUnorderedList<ushort, DynamicUnorderedUshorts>
    {
        public DynamicUnorderedUshorts() { }
        public DynamicUnorderedUshorts(List<ushort> list) : base(list) { }

        public override void CustomSerialize(Serializer serializer)
        {
            serializer.Serialize(ref list);
            if (serializer.IsDelta) serializer.Serialize(ref removed);
        }
    }

    public class DynamicOrderedCustomSerializables<T> : DynamicOrderedList<T, DynamicOrderedCustomSerializables<T>> where T : Serializer.ICustomSerializable, new()
    {
        public DynamicOrderedCustomSerializables() { }
        public DynamicOrderedCustomSerializables(List<T> list) : base(list) { }

        public override void CustomSerialize(Serializer serializer)
        {
            serializer.SerializeByte(ref list);
            if (serializer.IsDelta)
            {
                serializer.Serialize(ref listIndexes);
                serializer.Serialize(ref removedIndexes);
            }
        }
    }
    public class DynamicOrderedExtEnums<T> : DynamicOrderedList<T, DynamicOrderedExtEnums<T>> where T : ExtEnum<T>
    {
        public DynamicOrderedExtEnums() { }
        public DynamicOrderedExtEnums(List<T> list) : base(list) { }

        public override void CustomSerialize(Serializer serializer)
        {
            serializer.SerializeExtEnums<T>(ref list);
            if (serializer.IsDelta)
            {
                serializer.Serialize(ref listIndexes);
                serializer.Serialize(ref removedIndexes);
            }
        }
    }

    public class DynamicOrderedPlayerIDs : DynamicOrderedList<MeadowPlayerId, DynamicOrderedPlayerIDs>
    {
        public DynamicOrderedPlayerIDs() { }
        public DynamicOrderedPlayerIDs(List<MeadowPlayerId> list) : base(list) { }

        public override void CustomSerialize(Serializer serializer)
        {
            serializer.SerializePlayerIds(ref list);
            if (serializer.IsDelta)
            {
                serializer.Serialize(ref listIndexes);
                serializer.Serialize(ref removedIndexes);
            }
        }
    }

    public class DynamicOrderedEntityIDs : DynamicOrderedList<OnlineEntity.EntityId, DynamicOrderedEntityIDs>
    {
        public DynamicOrderedEntityIDs() { }
        public DynamicOrderedEntityIDs(List<OnlineEntity.EntityId> list) : base(list) { }

        public override void CustomSerialize(Serializer serializer)
        {
            serializer.SerializeByte(ref list);
            if (serializer.IsDelta)
            {
                serializer.Serialize(ref listIndexes);
                serializer.Serialize(ref removedIndexes);
            }
        }
    }

    public class DynamicOrderedEvents<T> : DynamicOrderedList<T, DynamicOrderedEvents<T>> where T : OnlineEvent
    {
        public DynamicOrderedEvents() { }
        public DynamicOrderedEvents(List<T> list) : base(list) { }

        public override void CustomSerialize(Serializer serializer)
        {
            serializer.SerializeEvents(ref list);
            if (serializer.IsDelta)
            {
                serializer.Serialize(ref listIndexes);
                serializer.Serialize(ref removedIndexes);
            }
        }
    }

    public class DynamicOrderedStates<T> : DynamicOrderedList<T, DynamicOrderedStates<T>> where T : OnlineState
    {
        public DynamicOrderedStates() { }
        public DynamicOrderedStates(List<T> list) : base(list) { }

        public override void CustomSerialize(Serializer serializer)
        {
            serializer.SerializePolyStatesByte(ref list);
            if (serializer.IsDelta)
            {
                serializer.Serialize(ref listIndexes);
                serializer.Serialize(ref removedIndexes);
            }
        }
    }

    public class DynamicOrderedStrings : DynamicOrderedList<string, DynamicOrderedStrings>
    {
        public DynamicOrderedStrings() { }
        public DynamicOrderedStrings(List<string> list) : base(list) { }

        public override void CustomSerialize(Serializer serializer)
        {
            serializer.Serialize(ref list);
            if (serializer.IsDelta)
            {
                serializer.Serialize(ref listIndexes);
                serializer.Serialize(ref removedIndexes);
            }
        }
    }

    public class DynamicOrderedUshorts : DynamicOrderedList<ushort, DynamicOrderedUshorts>
    {
        public DynamicOrderedUshorts() { }
        public DynamicOrderedUshorts(List<ushort> list) : base(list) { }

        public override void CustomSerialize(Serializer serializer)
        {
            serializer.Serialize(ref list);
            if (serializer.IsDelta)
            {
                serializer.Serialize(ref listIndexes);
                serializer.Serialize(ref removedIndexes);
            }
        }
    }

    public class FixedOrderedUshorts : FixedOrderedList<ushort, FixedOrderedUshorts>
    {
        public FixedOrderedUshorts() { }
        public FixedOrderedUshorts(List<ushort> list) : base(list) { }

        public override void CustomSerialize(Serializer serializer)
        {
            serializer.Serialize(ref list);
            if (serializer.IsDelta)
            {
                serializer.Serialize(ref updateIndexes);
            }
        }
    }

    public class UshortToByteDict : DynamicKVPList<ushort, byte, UshortToByteDict>
    {
        public UshortToByteDict() { }
        public UshortToByteDict(List<KeyValuePair<ushort, byte>> list) : base(list) { }

        public override void SerializeImpl(Serializer serializer)
        {
            serializer.Serialize(ref list);
            if (serializer.IsDelta)
                serializer.Serialize(ref removed);
        }
    }

    public class ByteToUshortDict : DynamicKVPList<byte, ushort, ByteToUshortDict>
    {
        public ByteToUshortDict() { }
        public ByteToUshortDict(List<KeyValuePair<byte, ushort>> list) : base(list) { }

        public override void SerializeImpl(Serializer serializer)
        {
            serializer.Serialize(ref list);
            if (serializer.IsDelta)
                serializer.Serialize(ref removed);
        }
    }
}