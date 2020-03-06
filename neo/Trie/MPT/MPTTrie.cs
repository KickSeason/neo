using Neo.IO.Json;
using Neo.Persistence;
using System;

namespace Neo.Trie.MPT
{
    public class MPTTrie : MPTReadOnlyTrie, ITrie
    {
        public static uint Version = 0;
        private MPTDb db;
        
        public MPTTrie(byte[] root, IKVStore store) : base(root, store)
        {
            this.db = new MPTDb(store);
        }

        public bool Put(byte[] path, byte[] value)
        {
            path = path.ToNibbles();
            if (value.Length == 0)
            {
                return TryDelete(ref root, path);
            }
            var n = new ValueNode(value);
            return Put(ref root, path, n);
        }

        private bool Put(ref MPTNode node, byte[] path, MPTNode val)
        {
            switch (node)
            {
                case ValueNode valueNode:
                    {
                        if (path.Length == 0 && val is ValueNode v)
                        {
                            node = v;
                            db.Put(node);
                            return true;
                        }
                        return false;
                    }
                case ShortNode shortNode:
                    {
                        var prefix = shortNode.Key.CommonPrefix(path);
                        if (prefix.Length == shortNode.Key.Length)
                        {
                            var result = Put(ref shortNode.Next, path.Skip(prefix.Length), val);
                            if (result)
                            {
                                shortNode.ResetFlag();
                                db.Put(shortNode);
                            }
                            return result;
                        }

                        var pathRemain = path.Skip(prefix.Length);
                        var keyRemain = shortNode.Key.Skip(prefix.Length);
                        var son = new FullNode();
                        MPTNode grandSon1 = HashNode.EmptyNode();
                        MPTNode grandSon2 = HashNode.EmptyNode();

                        Put(ref grandSon1, keyRemain.Skip(1), shortNode.Next);
                        son.Children[keyRemain[0]] = grandSon1;

                        if (pathRemain.Length == 0)
                        {
                            Put(ref grandSon2, pathRemain, val);
                            son.Children[FullNode.CHILD_COUNT - 1] = grandSon2;
                        }
                        else
                        {
                            Put(ref grandSon2, pathRemain.Skip(1), val);
                            son.Children[pathRemain[0]] = grandSon2;
                        }
                        db.Put(son);
                        if (prefix.Length > 0)
                        {
                            var extensionNode = new ShortNode()
                            {
                                Key = prefix,
                                Next = son,
                            };
                            db.Put(extensionNode);
                            node = extensionNode;
                        }
                        else
                        {
                            node = son;
                        }
                        return true;
                    }
                case FullNode fullNode:
                    {
                        var result = false;
                        if (path.Length == 0)
                        {
                            result = Put(ref fullNode.Children[FullNode.CHILD_COUNT - 1], path, val);
                        }
                        else
                        {
                            result = Put(ref fullNode.Children[path[0]], path.Skip(1), val);
                        }
                        if (result)
                        {
                            fullNode.ResetFlag();
                            db.Put(fullNode);
                        }
                        return result;
                    }
                case HashNode hashNode:
                    {
                        if (hashNode.IsEmptyNode)
                        {
                            var newNode = new ShortNode()
                            {
                                Key = path,
                                Next = val,
                            };
                            node = newNode;
                            if (val is ValueNode) db.Put(val);
                            db.Put(newNode);
                            return true;
                        }
                        node = Resolve(hashNode.Hash);
                        return Put(ref node, path, val);
                    }
                default:
                    throw new System.InvalidOperationException("Invalid node type.");
            }
        }

        public bool TryDelete(byte[] path)
        {
            path = path.ToNibbles();
            return TryDelete(ref root, path);
        }

        private bool TryDelete(ref MPTNode node, byte[] path)
        {
            switch (node)
            {
                case ValueNode valueNode:
                    {
                        if (path.Length == 0)
                        {
                            node = HashNode.EmptyNode();
                            return true;
                        }
                        return false;
                    }
                case ShortNode shortNode:
                    {
                        var prefix = shortNode.Key.CommonPrefix(path);
                        if (prefix.Length == shortNode.Key.Length)
                        {
                            var result = TryDelete(ref shortNode.Next, path.Skip(prefix.Length));
                            if (!result) return false;
                            if (shortNode.Next is HashNode hashNode && hashNode.IsEmptyNode)
                            {
                                node = shortNode.Next;
                                return true;
                            }
                            if (shortNode.Next is ShortNode sn)
                            {
                                shortNode.Key = shortNode.Key.Concat(sn.Key);
                                shortNode.Next = sn.Next;
                            }
                            shortNode.ResetFlag();
                            db.Put(shortNode);
                            return true;
                        }
                        return false;
                    }
                case FullNode fullNode:
                    {
                        var result = false;
                        if (path.Length == 0)
                        {
                            result = TryDelete(ref fullNode.Children[FullNode.CHILD_COUNT - 1], path);
                        }
                        else
                        {
                            result = TryDelete(ref fullNode.Children[path[0]], path.Skip(1));
                        }
                        if (!result) return false;
                        var childrenIndexes = Array.Empty<byte>();
                        for (int i = 0; i < FullNode.CHILD_COUNT; i++)
                        {
                            if (fullNode.Children[i] is HashNode hn && hn.IsEmptyNode) continue;
                            childrenIndexes = childrenIndexes.Add((byte)i);
                        }
                        if (childrenIndexes.Length > 1)
                        {
                            fullNode.ResetFlag();
                            db.Put(fullNode);
                            return true;
                        }
                        var lastChildIndex = childrenIndexes[0];
                        var lastChild = fullNode.Children[lastChildIndex];
                        if (lastChild is HashNode hashNode)
                            lastChild = Resolve(hashNode.Hash);
                        if (lastChild is ShortNode shortNode)
                        {
                            shortNode.Key = childrenIndexes.Concat(shortNode.Key);
                            shortNode.ResetFlag();
                            db.Put(shortNode);
                            node = shortNode;
                            return true;
                        }
                        var newNode = new ShortNode()
                        {
                            Key = childrenIndexes,
                            Next = lastChild,
                        };
                        node = newNode;
                        db.Put(newNode);
                        return true;
                    }
                case HashNode hashNode:
                    {
                        if (hashNode.IsEmptyNode)
                        {
                            return true;
                        }
                        node = Resolve(hashNode.Hash);
                        return TryDelete(ref node, path);
                    }
                default:
                    return false;
            }
        }

        public JObject ToJson()
        {
            return root.ToJson();
        }
    }
}