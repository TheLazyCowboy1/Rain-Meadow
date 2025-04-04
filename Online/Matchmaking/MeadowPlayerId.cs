﻿using System;
using Menu;

namespace RainMeadow
{
    public abstract class MeadowPlayerId : IEquatable<MeadowPlayerId>, Serializer.ICustomSerializable
    {
        public string name;

        public virtual string GetPersonaName() { return name; }
        public virtual void OpenProfileLink() {
            OnlineManager.instance.manager.ShowDialog(new DialogNotify(Utils.Translate("This player does not have a profile."), OnlineManager.instance.manager, null));
        }
        public virtual bool canOpenProfileLink { get => false; }

        protected MeadowPlayerId() { }
        protected MeadowPlayerId(string name)
        {
            this.name = name;
        }

        public abstract void CustomSerialize(Serializer serializer);
        public abstract bool Equals(MeadowPlayerId other);
        public override bool Equals(object obj)
        {
            return Equals(obj as MeadowPlayerId);
        }
        public abstract override int GetHashCode();
        public override string ToString()
        {
            return name;
        }
        public static bool operator ==(MeadowPlayerId lhs, MeadowPlayerId rhs)
        {
            return lhs is null ? rhs is null : lhs.Equals(rhs);
        }
        public static bool operator !=(MeadowPlayerId lhs, MeadowPlayerId rhs) => !(lhs == rhs);
    }
}