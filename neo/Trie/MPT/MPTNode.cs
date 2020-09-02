using Neo.Cryptography;
using Neo.IO;
using Neo.IO.Caching;
using Neo.IO.Json;
using System;
using System.IO;
using System.Text;

namespace Neo.Trie.MPT
{
    public abstract class MPTNode : ISerializable
    {
        private static ReflectionCache<byte> reflectionCache = ReflectionCache<byte>.CreateFromEnum<NodeType>();
        public static HashNode EmptyNode { get; } = new HashNode(null);
        public bool Dirty { get; private set; }
        protected abstract NodeType Type { get; }
        private UInt256 hash;
        public virtual int Size => sizeof(NodeType);
        public bool IsEmptyNode => this is HashNode hn && hn.Hash is null;

        protected virtual UInt256 GenHash()
        {
            return new UInt256(Crypto.Default.Hash256(this.ToArray()));
        }

        public virtual UInt256 GetHash()
        {
            if (!Dirty && !(hash is null)) return hash;
            hash = GenHash();
            Dirty = false;
            return hash;
        }

        public void SetDirty()
        {
            Dirty = true;
        }

        public MPTNode()
        {
            Dirty = true;
        }

        public virtual void Serialize(BinaryWriter writer)
        {
            writer.Write((byte)Type);
        }

        public virtual void SerializeAsChild(BinaryWriter writer)
        {
            var hn = new HashNode(GetHash());
            hn.SerializeAsChild(writer);
        }

        public abstract void Deserialize(BinaryReader reader);

        public static MPTNode DeserializeFromByteArray(byte[] data)
        {
            if (data is null || data.Length < 1) return null;

            using (MemoryStream stream = new MemoryStream(data))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                MPTNode n = (MPTNode)reflectionCache.CreateInstance(reader.ReadByte());
                if (n is null) throw new InvalidOperationException("Invalid mpt node type");
                n.Deserialize(reader);
                return n;
            }
        }

        public abstract JObject ToJson();
    }
}