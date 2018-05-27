﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CascStorageLib
{
    public class WDC1Row : IDB2Row
    {
        private BitReader m_data;
        private DB2Reader m_reader;
        private int m_dataOffset;

        public int Id { get; set; }
        public int RecordIndex { get; set; }

        private FieldMetaData[] m_fieldMeta;
        private ColumnMetaData[] m_columnMeta;
        private Value32[][] m_palletData;
        private Dictionary<int, Value32>[] m_commonData;
        private ReferenceEntry? m_refData;

        public WDC1Row(DB2Reader reader, BitReader data, int id, ReferenceEntry? refData)
        {
            m_reader = reader;
            m_data = data;

            m_dataOffset = m_data.Offset;

            m_fieldMeta = reader.Meta;
            m_columnMeta = reader.ColumnMeta;
            m_palletData = reader.PalletData;
            m_commonData = reader.CommonData;
            m_refData = refData;

            if (id != -1)
                Id = id;
            else
            {
                int idFieldIndex = reader.IdFieldIndex;

                m_data.Position = m_columnMeta[idFieldIndex].RecordOffset;

                Id = GetFieldValue<int>(0, m_data, m_fieldMeta[idFieldIndex], m_columnMeta[idFieldIndex], m_palletData[idFieldIndex], m_commonData[idFieldIndex]);
            }
        }

        private static Dictionary<Type, Func<int, BitReader, FieldMetaData, ColumnMetaData, Value32[], Dictionary<int, Value32>, Dictionary<long, string>, DB2Reader, object>> simpleReaders = new Dictionary<Type, Func<int, BitReader, FieldMetaData, ColumnMetaData, Value32[], Dictionary<int, Value32>, Dictionary<long, string>, DB2Reader, object>>
        {
            [typeof(long)] = (id, data, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<long>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(float)] = (id, data, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<float>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(int)] = (id, data, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<int>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(uint)] = (id, data, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<uint>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(short)] = (id, data, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<short>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(ushort)] = (id, data, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<ushort>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(sbyte)] = (id, data, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<sbyte>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(byte)] = (id, data, fieldMeta, columnMeta, palletData, commonData, stringTable, header) => GetFieldValue<byte>(id, data, fieldMeta, columnMeta, palletData, commonData),
            [typeof(string)] = (id, data, fieldMeta, columnMeta, palletData, commonData, stringTable, header) =>
            {
                if (header.Flags.HasFlag(DB2Flags.Sparse))
                {
                    return data.ReadCString();
                }
                else
                {
                    int strOfs = GetFieldValue<int>(id, data, fieldMeta, columnMeta, palletData, commonData);
                    return stringTable[strOfs];
                }
            },
        };

        private static Dictionary<Type, Func<BitReader, FieldMetaData, ColumnMetaData, Value32[], Dictionary<int, Value32>, Dictionary<long, string>, int, object>> arrayReaders = new Dictionary<Type, Func<BitReader, FieldMetaData, ColumnMetaData, Value32[], Dictionary<int, Value32>, Dictionary<long, string>, int, object>>
        {
            [typeof(float[])] = (data, fieldMeta, columnMeta, palletData, commonData, stringTable, arrayIndex) => GetFieldValueArray<float>(data, fieldMeta, columnMeta, palletData, commonData, arrayIndex),
            [typeof(int[])] = (data, fieldMeta, columnMeta, palletData, commonData, stringTable, arrayIndex) => GetFieldValueArray<int>(data, fieldMeta, columnMeta, palletData, commonData, arrayIndex),
            [typeof(uint[])] = (data, fieldMeta, columnMeta, palletData, commonData, stringTable, arrayIndex) => GetFieldValueArray<uint>(data, fieldMeta, columnMeta, palletData, commonData, arrayIndex),
            [typeof(ulong[])] = (data, fieldMeta, columnMeta, palletData, commonData, stringTable, arrayIndex) => GetFieldValueArray<ulong>(data, fieldMeta, columnMeta, palletData, commonData, arrayIndex),
            [typeof(ushort[])] = (data, fieldMeta, columnMeta, palletData, commonData, stringTable, arrayIndex) => GetFieldValueArray<ushort>(data, fieldMeta, columnMeta, palletData, commonData, arrayIndex),
            [typeof(short[])] = (data, fieldMeta, columnMeta, palletData, commonData, stringTable, arrayIndex) => GetFieldValueArray<short>(data, fieldMeta, columnMeta, palletData, commonData, arrayIndex),
            [typeof(byte[])] = (data, fieldMeta, columnMeta, palletData, commonData, stringTable, arrayIndex) => GetFieldValueArray<byte>(data, fieldMeta, columnMeta, palletData, commonData, arrayIndex),
            [typeof(sbyte[])] = (data, fieldMeta, columnMeta, palletData, commonData, stringTable, arrayIndex) => GetFieldValueArray<sbyte>(data, fieldMeta, columnMeta, palletData, commonData, arrayIndex),
            [typeof(string[])] = (data, fieldMeta, columnMeta, palletData, commonData, stringTable, arrayIndex) =>
            {
                int strOfs = GetFieldValueArray<int>(data, fieldMeta, columnMeta, palletData, commonData, arrayIndex)[0];
                return stringTable[strOfs];
            },
        };

        public T GetField<T>(int fieldIndex, int arrayIndex = -1)
        {
            object value = null;

            if (fieldIndex >= m_reader.Meta.Length)
            {
                value = m_refData?.Id ?? 0;
                return (T)value;
            }

            m_data.Position = m_columnMeta[fieldIndex].RecordOffset;
            m_data.Offset = m_dataOffset;

            if (arrayIndex >= 0)
            {
                if (arrayReaders.TryGetValue(typeof(T), out var reader))
                    value = reader(m_data, m_fieldMeta[fieldIndex], m_columnMeta[fieldIndex], m_palletData[fieldIndex], m_commonData[fieldIndex], m_reader.StringTable, arrayIndex);
                else
                    throw new Exception("Unhandled array type: " + typeof(T).Name);
            }
            else
            {
                if (simpleReaders.TryGetValue(typeof(T), out var reader))
                    value = reader(Id, m_data, m_fieldMeta[fieldIndex], m_columnMeta[fieldIndex], m_palletData[fieldIndex], m_commonData[fieldIndex], m_reader.StringTable, m_reader);
                else
                    throw new Exception("Unhandled field type: " + typeof(T).Name);
            }

            return (T)value;
        }

        public void GetFields<T>(FieldCache<T>[] fields, T entry)
        {
            //Store start position
            int offSet = m_data.Position;
            int recordSize = m_reader.Flags.HasFlag(DB2Flags.Sparse) ? (int)m_reader.sparseEntries[RecordIndex].Size * 8 : 0;

            for (int i = 0; i < fields.Length; ++i)
            {
                FieldCache<T> info = fields[i];
                object value = null;
                int fieldIndex = i;

                if (fieldIndex >= m_reader.Meta.Length)
                {
                    value = m_refData?.Id ?? 0;
                    info.Setter(entry, Convert.ChangeType(value, info.Field.FieldType));
                    continue;
                }

                if (!m_reader.Flags.HasFlag(DB2Flags.Sparse))
                {
                    m_data.Position = m_columnMeta[fieldIndex].RecordOffset;
                    m_data.Offset = m_dataOffset;
                }

                if (info.ArraySize >= 0)
                {
                    if (arrayReaders.TryGetValue(info.Field.FieldType, out var reader))
                        value = reader(m_data, m_fieldMeta[fieldIndex], m_columnMeta[fieldIndex], m_palletData[fieldIndex], m_commonData[fieldIndex], m_reader.StringTable, info.ArraySize);
                    else
                        throw new Exception("Unhandled array type: " + typeof(T).Name);
                }
                else
                {
                    if (simpleReaders.TryGetValue(info.Field.FieldType, out var reader))
                        value = reader(Id, m_data, m_fieldMeta[fieldIndex], m_columnMeta[fieldIndex], m_palletData[fieldIndex], m_commonData[fieldIndex], m_reader.StringTable, m_reader);
                    else
                        throw new Exception("Unhandled field type: " + typeof(T).Name);
                }

                info.Setter(entry, value);
            }

            if (m_data.Position - offSet < recordSize)
                m_data.Position += (recordSize - (m_data.Position - offSet));
        }

        private static T GetFieldValue<T>(int Id, BitReader r, FieldMetaData fieldMeta, ColumnMetaData columnMeta, Value32[] palletData, Dictionary<int, Value32> commonData) where T : struct
        {
            switch (columnMeta.CompressionType)
            {
                case CompressionType.None:
                    int bitSize = 32 - fieldMeta.Bits;
                    if (bitSize > 0)
                        return r.ReadValue64(bitSize).GetValue<T>();
                    else
                        return r.ReadValue64(columnMeta.Immediate.BitWidth).GetValue<T>();
                case CompressionType.Immediate:
                    return r.ReadValue64(columnMeta.Immediate.BitWidth).GetValue<T>();
                case CompressionType.Common:
                    if (commonData.TryGetValue(Id, out Value32 val))
                        return val.GetValue<T>();
                    else
                        return columnMeta.Common.DefaultValue.GetValue<T>();
                case CompressionType.Pallet:
                    uint palletIndex = r.ReadUInt32(columnMeta.Pallet.BitWidth);

                    T val1 = palletData[palletIndex].GetValue<T>();

                    return val1;
            }
            throw new Exception(string.Format("Unexpected compression type {0}", columnMeta.CompressionType));
        }

        private static T[] GetFieldValueArray<T>(BitReader r, FieldMetaData fieldMeta, ColumnMetaData columnMeta, Value32[] palletData, Dictionary<int, Value32> commonData, int arraySize) where T : struct
        {
            switch (columnMeta.CompressionType)
            {
                case CompressionType.None:
                    int bitSize = 32 - fieldMeta.Bits;

                    T[] arr1 = new T[arraySize];

                    for (int i = 0; i < arr1.Length; i++)
                    {
                        if (bitSize > 0)
                            arr1[i] = r.ReadValue64(bitSize).GetValue<T>();
                        else
                            arr1[i] = r.ReadValue64(columnMeta.Immediate.BitWidth).GetValue<T>();
                    }

                    return arr1;
                case CompressionType.Immediate:
                    T[] arr2 = new T[arraySize];

                    for (int i = 0; i < arr2.Length; i++)
                        arr2[i] = r.ReadValue64(columnMeta.Immediate.BitWidth).GetValue<T>();

                    return arr2;
                case CompressionType.PalletArray:
                    int cardinality = columnMeta.Pallet.Cardinality;

                    if (arraySize != cardinality)
                        throw new Exception("Struct missmatch for pallet array field?");

                    uint palletArrayIndex = r.ReadUInt32(columnMeta.Pallet.BitWidth);

                    T[] arr3 = new T[cardinality];

                    for (int i = 0; i < arr3.Length; i++)
                        arr3[i] = palletData[i + cardinality * (int)palletArrayIndex].GetValue<T>();

                    return arr3;
            }
            throw new Exception(string.Format("Unexpected compression type {0}", columnMeta.CompressionType));
        }

        public IDB2Row Clone()
        {
            return (IDB2Row)MemberwiseClone();
        }
    }

    public class WDC1Reader : DB2Reader
    {
        private const int HeaderSize = 84;
        private const uint WDC1FmtSig = 0x31434457; // WDC1

        public WDC1Reader(string dbcFile) : this(new FileStream(dbcFile, FileMode.Open)) { }

        public WDC1Reader(Stream stream)
        {
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.BaseStream.Length < HeaderSize)
                    throw new InvalidDataException(String.Format("WDC1 file is corrupted!"));

                uint magic = reader.ReadUInt32();

                if (magic != WDC1FmtSig)
                    throw new InvalidDataException(String.Format("WDC1 file is corrupted!"));

                RecordsCount = reader.ReadInt32();
                FieldsCount = reader.ReadInt32();
                RecordSize = reader.ReadInt32();
                StringTableSize = reader.ReadInt32();

                TableHash = reader.ReadUInt32();
                LayoutHash = reader.ReadUInt32();
                MinIndex = reader.ReadInt32();
                MaxIndex = reader.ReadInt32();
                int locale = reader.ReadInt32();
                int copyTableSize = reader.ReadInt32();
                Flags = (DB2Flags)reader.ReadUInt16();
                IdFieldIndex = reader.ReadUInt16();

                int totalFieldsCount = reader.ReadInt32();
                int packedDataOffset = reader.ReadInt32(); // Offset within the field where packed data starts
                int lookupColumnCount = reader.ReadInt32(); // count of lookup columns
                int sparseTableOffset = reader.ReadInt32(); // absolute value, {uint offset, ushort size}[MaxId - MinId + 1]
                int indexDataSize = reader.ReadInt32(); // int indexData[IndexDataSize / 4]
                int columnMetaDataSize = reader.ReadInt32(); // 24 * NumFields bytes, describes column bit packing, {ushort recordOffset, ushort size, uint additionalDataSize, uint compressionType, uint packedDataOffset or commonvalue, uint cellSize, uint cardinality}[NumFields], sizeof(DBC2CommonValue) == 8
                int commonDataSize = reader.ReadInt32();
                int palletDataSize = reader.ReadInt32(); // in bytes, sizeof(DBC2PalletValue) == 4
                int referenceDataSize = reader.ReadInt32(); // uint NumRecords, uint minId, uint maxId, {uint id, uint index}[NumRecords], questionable usefulness...

                // field meta data
                m_meta = reader.ReadArray<FieldMetaData>(FieldsCount);

                if (!Flags.HasFlag(DB2Flags.Sparse))
                {
                    // records data
                    recordsData = reader.ReadBytes(RecordsCount * RecordSize);

                    Array.Resize(ref recordsData, recordsData.Length + 8); // pad with extra zeros so we don't crash when reading

                    // string data
                    m_stringsTable = new Dictionary<long, string>();

                    for (int i = 0; i < StringTableSize;)
                    {
                        long oldPos = reader.BaseStream.Position;

                        m_stringsTable[i] = reader.ReadCString();

                        i += (int)(reader.BaseStream.Position - oldPos);
                    }
                }
                else
                {
                    // sparse data with inlined strings
                    sparseData = reader.ReadBytes(sparseTableOffset - HeaderSize - Marshal.SizeOf<FieldMetaData>() * FieldsCount);

                    recordsData = sparseData;

                    if (reader.BaseStream.Position != sparseTableOffset)
                        throw new Exception("r.BaseStream.Position != sparseTableOffset");

                    Dictionary<uint, int> offSetKeyMap = new Dictionary<uint, int>();
                    List<SparseEntry> tempSparseEntries = new List<SparseEntry>();
                    for (int i = 0; i < (MaxIndex - MinIndex + 1); i++)
                    {
                        SparseEntry sparse = reader.Read<SparseEntry>();

                        if (sparse.Offset == 0 || sparse.Size == 0)
                            continue;

                        // special case, may contain duplicates in the offset map that we don't want
                        if (copyTableSize == 0)
                        {
                            if (offSetKeyMap.ContainsKey(sparse.Offset))
                                continue;
                        }

                        tempSparseEntries.Add(sparse);
                        offSetKeyMap.Add(sparse.Offset, 0);
                    }

                    sparseEntries = tempSparseEntries.ToArray();
                }

                // index data
                m_indexData = reader.ReadArray<int>(indexDataSize / 4);

                // duplicate rows data
                Dictionary<int, int> copyData = new Dictionary<int, int>();

                for (int i = 0; i < copyTableSize / 8; i++)
                    copyData[reader.ReadInt32()] = reader.ReadInt32();

                // column meta data
                m_columnMeta = reader.ReadArray<ColumnMetaData>(FieldsCount);

                // pallet data
                m_palletData = new Value32[m_columnMeta.Length][];

                for (int i = 0; i < m_columnMeta.Length; i++)
                {
                    if (m_columnMeta[i].CompressionType == CompressionType.Pallet || m_columnMeta[i].CompressionType == CompressionType.PalletArray)
                    {
                        m_palletData[i] = reader.ReadArray<Value32>((int)m_columnMeta[i].AdditionalDataSize / 4);
                    }
                }

                // common data
                m_commonData = new Dictionary<int, Value32>[m_columnMeta.Length];

                for (int i = 0; i < m_columnMeta.Length; i++)
                {
                    if (m_columnMeta[i].CompressionType == CompressionType.Common)
                    {
                        Dictionary<int, Value32> commonValues = new Dictionary<int, Value32>();
                        m_commonData[i] = commonValues;

                        for (int j = 0; j < m_columnMeta[i].AdditionalDataSize / 8; j++)
                            commonValues[reader.ReadInt32()] = reader.Read<Value32>();
                    }
                }

                // reference data
                ReferenceData refData = null;

                if (referenceDataSize > 0)
                {
                    refData = new ReferenceData
                    {
                        NumRecords = reader.ReadInt32(),
                        MinId = reader.ReadInt32(),
                        MaxId = reader.ReadInt32()
                    };

                    refData.Entries = reader.ReadArray<ReferenceEntry>(refData.NumRecords);
                }

                BitReader bitReader = new BitReader(recordsData);

                for (int i = 0; i < RecordsCount; ++i)
                {
                    bitReader.Position = 0;
                    bitReader.Offset = i * RecordSize;

                    IDB2Row rec = new WDC1Row(this, bitReader, indexDataSize != 0 ? m_indexData[i] : -1, refData?.Entries[i]);
                    rec.RecordIndex = i;

                    if (indexDataSize != 0)
                        _Records.Add(m_indexData[i], rec);
                    else
                        _Records.Add(rec.Id, rec);

                    if (i % 1000 == 0)
                        Console.Write("\r{0} records read", i);
                }

                if (Flags.HasFlag(DB2Flags.Sparse))
                    bitReader.Offset = 0;

                foreach (var copyRow in copyData)
                {
                    IDB2Row rec = _Records[copyRow.Value].Clone();
                    rec.Id = copyRow.Key;
                    _Records.Add(copyRow.Key, rec);
                }
            }
        }
    }
}
