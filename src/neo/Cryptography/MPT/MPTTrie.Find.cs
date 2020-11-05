using Neo.IO;
using Neo.IO.Caching;
using System;
using System.Collections.Generic;
using System.Linq;
using static Neo.Helper;

namespace Neo.Cryptography.MPT
{
    partial class MPTTrie<TKey, TValue>
    {
        private ReadOnlySpan<byte> Seek(ref MPTNode node, ReadOnlySpan<byte> path, out MPTNode start)
        {
            switch (node)
            {
                case LeafNode leafNode:
                    {
                        if (path.IsEmpty)
                        {
                            start = leafNode;
                            return ReadOnlySpan<byte>.Empty;
                        }
                        break;
                    }
                case HashNode hashNode:
                    {
                        if (hashNode.IsEmpty) break;
                        var newNode = Resolve(hashNode);
                        if (newNode is null) break;
                        node = newNode;
                        return Seek(ref node, path, out start);
                    }
                case BranchNode branchNode:
                    {
                        if (path.IsEmpty)
                        {
                            start = branchNode;
                            return ReadOnlySpan<byte>.Empty;
                        }
                        return Concat(path[..1], Seek(ref branchNode.Children[path[0]], path[1..], out start));
                    }
                case ExtensionNode extensionNode:
                    {
                        if (path.IsEmpty)
                        {
                            start = extensionNode.Next;
                            return extensionNode.Key;
                        }
                        if (path.StartsWith(extensionNode.Key))
                        {
                            return Concat(extensionNode.Key, Seek(ref extensionNode.Next, path[extensionNode.Key.Length..], out start));
                        }
                        if (extensionNode.Key.AsSpan().StartsWith(path))
                        {
                            start = extensionNode.Next;
                            return extensionNode.Key;
                        }
                        break;
                    }
            }
            start = null;
            return ReadOnlySpan<byte>.Empty;
        }

        public IEnumerable<(TKey Key, TValue Value)> Find(ReadOnlySpan<byte> prefix)
        {
            var path = ToNibbles(prefix);
            path = Seek(ref root, path, out MPTNode start).ToArray();
            return Travers(start, path)
                .Select(p => (FromNibbles(p.Key).AsSerializable<TKey>(), p.Value.AsSerializable<TValue>()));
        }

        private IEnumerable<(byte[] Key, byte[] Value)> Travers(MPTNode node, byte[] path)
        {
            if (node is null) yield break;
            switch (node)
            {
                case LeafNode leafNode:
                    {
                        yield return (path, (byte[])leafNode.Value.Clone());
                        break;
                    }
                case HashNode hashNode:
                    {
                        if (hashNode.IsEmpty) break;
                        var newNode = Resolve(hashNode);
                        if (newNode is null) break;
                        node = newNode;
                        foreach (var item in Travers(node, path))
                            yield return item;
                        break;
                    }
                case BranchNode branchNode:
                    {
                        for (int i = 0; i < BranchNode.ChildCount; i++)
                        {
                            foreach (var item in Travers(branchNode.Children[i], i == BranchNode.ChildCount - 1 ? path : Concat(path, new byte[] { (byte)i })))
                                yield return item;
                        }
                        break;
                    }
                case ExtensionNode extensionNode:
                    {
                        foreach (var item in Travers(extensionNode.Next, Concat(path, extensionNode.Key)))
                            yield return item;
                        break;
                    }
            }
        }

        public IEnumerable<(TKey Key, TValue Value)> Seek(ReadOnlySpan<byte> key, SeekDirection direction)
        {
            var path = ToNibbles(key);
            return TraversDirection(root, path, Array.Empty<byte>(), direction, path.Length == 0)
                .Select(p => (FromNibbles(p.Key).AsSerializable<TKey>(), p.Value.AsSerializable<TValue>()));
        }

        private IEnumerable<(byte[] Key, byte[] Value)> TraversDirection(MPTNode node, byte[] path, byte[] key, SeekDirection direction, bool exact = false)
        {
            if (node is null) yield break;
            switch (node)
            {
                case LeafNode leafNode:
                    {
                        yield return (key, (byte[])leafNode.Value.Clone());
                        break;
                    }
                case HashNode hashNode:
                    {
                        if (hashNode.IsEmpty) break;
                        var newNode = Resolve(hashNode);
                        if (newNode is null) throw new KeyNotFoundException("Internal error, can't resolve hash when mpt find");
                        node = newNode;
                        foreach (var item in TraversDirection(node, path, key, direction, exact))
                            yield return item;
                        break;
                    }
                case BranchNode branchNode:
                    {
                        if (path.Length == 0)
                        {
                            if (!exact || direction == SeekDirection.Forward)
                            {
                                if (direction == SeekDirection.Forward)
                                {
                                    foreach (var item in TraversDirection(branchNode.Children[BranchNode.ChildCount - 1], Array.Empty<byte>(), key, direction))
                                        yield return item;
                                }
                                int start = direction == SeekDirection.Forward ? 0 : BranchNode.ChildCount - 2;
                                for (int i = start; 0 <= i && i < BranchNode.ChildCount - 1; i = i + (int)direction)
                                {
                                    foreach (var item in TraversDirection(branchNode.Children[i], path, Concat(key, new byte[] { (byte)i }), direction))
                                        yield return item;
                                }
                            }
                            if (direction == SeekDirection.Backward)
                            {
                                foreach (var item in TraversDirection(branchNode.Children[BranchNode.ChildCount - 1], path, key, direction))
                                    yield return item;
                            }
                        }
                        else
                        {
                            for (int i = path[0]; 0 <= i && i < BranchNode.ChildCount - 1; i = i + (int)direction)
                            {
                                if (i == path[0])
                                {
                                    foreach (var item in TraversDirection(branchNode.Children[i], path[1..], Concat(key, new byte[] { (byte)i }), direction, path[1..].Length == 0))
                                        yield return item;
                                }
                                else
                                {
                                    foreach (var item in TraversDirection(branchNode.Children[i], Array.Empty<byte>(), Concat(key, new byte[] { (byte)i }), direction))
                                        yield return item;
                                }
                            }
                            if (direction == SeekDirection.Backward)
                            {
                                foreach (var item in TraversDirection(branchNode.Children[BranchNode.ChildCount - 1], Array.Empty<byte>(), key, direction))
                                    yield return item;
                            }
                        }
                        break;
                    }
                case ExtensionNode extensionNode:
                    {
                        if (path.Length == 0)
                        {
                            if (exact && direction == SeekDirection.Backward) break;
                            foreach (var item in TraversDirection(extensionNode.Next, path, Concat(key, extensionNode.Key), direction))
                                yield return item;
                        }
                        else
                        {
                            ByteArrayComparer comparer = direction == SeekDirection.Forward ? ByteArrayComparer.Default : ByteArrayComparer.Reverse;
                            int len = Math.Min(extensionNode.Key.Length, path.Length);
                            int result = comparer.Compare(extensionNode.Key[..len], path[..len]);
                            if (result < 0) break;
                            if (0 < result)
                            {
                                foreach (var item in TraversDirection(extensionNode.Next, Array.Empty<byte>(), Concat(key, extensionNode.Key), direction))
                                    yield return item;
                                break;
                            }
                            if (extensionNode.Key.Length == path.Length)
                            {
                                foreach (var item in TraversDirection(extensionNode.Next, Array.Empty<byte>(), Concat(key, extensionNode.Key), direction, true))
                                    yield return item;
                                break;
                            }
                            if (path.Length < extensionNode.Key.Length)
                            {
                                if (direction == SeekDirection.Backward) break;
                                foreach (var item in TraversDirection(extensionNode.Next, Array.Empty<byte>(), Concat(key, extensionNode.Key), direction))
                                    yield return item;
                                break;
                            }
                            foreach (var item in TraversDirection(extensionNode.Next, path[extensionNode.Key.Length..], Concat(key, extensionNode.Key), direction))
                                yield return item;
                            break;
                        }
                        break;
                    }
            }
        }
    }
}
