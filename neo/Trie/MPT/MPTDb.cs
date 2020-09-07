
using System.Collections.Generic;

namespace Neo.Trie.MPT
{
    public enum TrackState : byte
    {
        None,
        Put,
        Deleted
    }

    public class Trackable
    {
        public byte[] RawNode;
        public TrackState State;
    }

    public class MPTDb
    {
        private IKVStore store;
        private Dictionary<UInt256, Trackable> cache = new Dictionary<UInt256, Trackable>();

        public MPTDb(IKVStore store)
        {
            this.store = store;
        }

        public MPTNode Node(UInt256 hash)
        {
            if (hash is null) return null;
            if (cache.TryGetValue(hash, out Trackable t))
            {
                return MPTNode.DeserializeFromByteArray(t.RawNode);
            }
            var data = store.Get(hash.ToArray());
            cache[hash] = new Trackable
            {
                RawNode = data,
                State = TrackState.None,
            };
            return MPTNode.DeserializeFromByteArray(data);
        }

        public void Put(MPTNode node)
        {
            if (node is HashNode hn)
            {
                throw new System.InvalidOperationException("Means nothing to store HashNode");
            }
            cache[node.GetHash()] = new Trackable
            {
                RawNode = node.ToArrayWithReferences(),
                State = TrackState.Put,
            };
        }

        public void Delete(UInt256 hash)
        {
            cache[hash] = new Trackable
            {
                RawNode = null,
                State = TrackState.Deleted,
            };
        }

        public void Commit()
        {
            foreach (var item in cache)
            {
                switch (item.Value.State)
                {
                    case TrackState.Put:
                        store.Put(item.Key.ToArray(), item.Value.RawNode);
                        break;
                    case TrackState.Deleted:
                        store.Delete(item.Key.ToArray());
                        break;
                }
            }
        }
    }
}
