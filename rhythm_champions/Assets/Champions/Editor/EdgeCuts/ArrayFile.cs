using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Champions.EditorTools.EdgeCuts
{
    /// <summary>One array in an <see cref="ArrayFile"/>: its name, element type (f4 or i4), shape and byte offset.</summary>
    [Serializable]
    public sealed class ArrayInfo
    {
        public string name;
        public string type;
        public int[] shape;
        public long offset;

        public int Count
        {
            get
            {
                int n = 1;
                foreach (var d in shape) n *= d;
                return n;
            }
        }
    }

    /// <summary>
    /// The edge-cut pipeline's exchange format between Unity and the recipes (tools/src/champions_tools/edgecut/arrays.py
    /// reads and writes the same): a JSON header beside a `.bytes` file of little-endian float32 (f4) and int32 (i4)
    /// arrays, each listed in the header's `arrays` with its shape and offset. The header's other fields are the
    /// writer's own.
    /// </summary>
    public sealed class ArrayFile
    {
        readonly Dictionary<string, ArrayInfo> _infos = new Dictionary<string, ArrayInfo>();
        readonly byte[] _data;

        [Serializable]
        sealed class Index
        {
            public ArrayInfo[] arrays = Array.Empty<ArrayInfo>();
        }

        ArrayFile(byte[] data, ArrayInfo[] infos)
        {
            _data = data;
            foreach (var a in infos) _infos[a.name] = a;
        }

        /// <summary>The bytes beside a header: `x.json`'s are `x.bytes`.</summary>
        public static string BytesPath(string json) => Path.ChangeExtension(json, ".bytes");

        public static ArrayFile Read(string json)
        {
            var index = JsonUtility.FromJson<Index>(File.ReadAllText(json));
            return new ArrayFile(File.ReadAllBytes(BytesPath(json)), index.arrays);
        }

        public bool Has(string name) => _infos.ContainsKey(name);

        public ArrayInfo Info(string name) =>
            _infos.TryGetValue(name, out var a) ? a : throw new Exception($"ArrayFile: no array '{name}'");

        public float[] Floats(string name) => Get<float>(name, "f4");

        public int[] Ints(string name) => Get<int>(name, "i4");

        T[] Get<T>(string name, string type) where T : struct
        {
            var a = Info(name);
            if (a.type != type) throw new Exception($"ArrayFile: '{name}' is {a.type}, not {type}");
            var o = new T[a.Count];
            Buffer.BlockCopy(_data, (int)a.offset, o, 0, o.Length * 4);
            return o;
        }

        /// <summary>Gathers arrays for <see cref="Write"/>, assigning their offsets in order.</summary>
        public sealed class Builder
        {
            readonly List<ArrayInfo> _infos = new List<ArrayInfo>();
            readonly MemoryStream _bytes = new MemoryStream();

            public ArrayInfo[] Infos => _infos.ToArray();

            public void Add(string name, float[] values, params int[] shape) => Add(name, "f4", values, values.Length, shape);

            public void Add(string name, int[] values, params int[] shape) => Add(name, "i4", values, values.Length, shape);

            void Add(string name, string type, Array values, int count, int[] shape)
            {
                var info = new ArrayInfo { name = name, type = type, shape = shape, offset = _bytes.Length };
                if (info.Count != count) throw new Exception($"ArrayFile: '{name}' has {count} values for shape [{string.Join(", ", shape)}]");
                var raw = new byte[count * 4];
                Buffer.BlockCopy(values, 0, raw, 0, raw.Length);
                _bytes.Write(raw, 0, raw.Length);
                _infos.Add(info);
            }

            /// <summary>Writes the header (the caller's JSON, whose `arrays` must be <see cref="Infos"/>) and the bytes.</summary>
            public void Write(string json, string header)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(json)));
                File.WriteAllText(json, header);
                File.WriteAllBytes(BytesPath(json), _bytes.ToArray());
            }
        }
    }
}
