using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SentisFlatBuffer;
using Unity.InferenceEngine;
using Unity.InferenceEngine.Google.FlatBuffers;

namespace Hypocycloid.Editor.Sentis
{
    public static class SentisStreamingModelWriter
    {
        const int WeightsFlatBufferOverhead = 32;
        const long MaxConstantSize = int.MaxValue - WeightsFlatBufferOverhead;

        // Sentis reads weight segments sequentially as size-prefixed flatbuffers, so segment
        // count is free. Small segments keep the transient constantData + FlatBufferBuilder
        // pair during Save at ~2x this size instead of ~2x 2 GB, which OOMs a pagefile-less
        // 64 GB machine while the editor already holds the whole in-memory Model.
        const long TargetSegmentSize = 256L * 1024 * 1024;
        const int FormatVersion = 8;

        public static void Save(string fileName, Model model)
        {
            using FileStream stream = File.Create(fileName);
            Save(stream, model);
        }

        static VectorOffset CreateDynamicSizes(FlatBufferBuilder builder, DynamicTensorShape shape)
        {
            if (shape.isRankDynamic)
                return default;

            Offset<EDim>[] dims = new Offset<EDim>[shape.rank];
            for (int i = 0; i < shape.rank; i++)
            {
                DynamicTensorDim dim = shape[i];
                dims[i] = dim.dimType switch
                {
                    DimType.Unknown => EDim.CreateEDim(builder, val_type: SymbolicDim.NONE),
                    DimType.Static => EDim.CreateEDim(
                        builder,
                        val_type: SymbolicDim.Int,
                        Int.CreateInt(builder, dim.value).Value
                    ),
                    DimType.Param => EDim.CreateEDim(
                        builder,
                        val_type: SymbolicDim.Byte,
                        SentisFlatBuffer.Byte.CreateByte(builder, dim.param).Value
                    ),
                    _ => throw new ArgumentOutOfRangeException(),
                };
            }
            return SentisFlatBuffer.Tensor.CreateDynamicSizesVector(builder, dims);
        }

        static void Save(Stream stream, Model model)
        {
            model.InferDataTypesShapes();
            FlatBufferBuilder builder = new FlatBufferBuilder(1);
            List<Offset<EValue>> values = new List<Offset<EValue>>();
            Dictionary<int, int> indexMapping = new Dictionary<int, int>();

            int[] inputIndices = new int[model.inputs.Count];
            StringOffset[] inputNames = new StringOffset[model.inputs.Count];
            for (int i = 0; i < model.inputs.Count; i++)
            {
                Model.Input input = model.inputs[i];
                inputIndices[i] = i;
                inputNames[i] = builder.CreateString(input.name);

                Offset<SentisFlatBuffer.Tensor> value;
                if (input.shape.IsStatic())
                {
                    TensorShape shape = input.shape.ToTensorShape();
                    VectorOffset size = SentisFlatBuffer.Tensor.CreateFixedSizesVector(
                        builder,
                        shape.ToArray()
                    );
                    int byteLength =
                        shape.length
                        * AllocatorUtils.LengthInBytesPadded(shape.length, input.dataType);
                    value = SentisFlatBuffer.Tensor.CreateTensor(
                        builder,
                        (ScalarType)input.dataType,
                        byteLength,
                        size
                    );
                }
                else
                {
                    VectorOffset size = CreateDynamicSizes(builder, input.shape);
                    value = SentisFlatBuffer.Tensor.CreateTensor(
                        builder,
                        (ScalarType)input.dataType,
                        shape_dynamism: TensorShapeDynamism.DYNAMIC_UNBOUND,
                        dynamic_sizesOffset: size
                    );
                }
                values.Add(EValue.CreateEValue(builder, KernelTypes.Tensor, value.Value));
                indexMapping[input.index] = values.Count - 1;
            }

            VectorOffset planInputs = ExecutionPlan.CreateInputsVector(builder, inputIndices);
            VectorOffset planInputNames = ExecutionPlan.CreateInputsNameVector(builder, inputNames);
            StringOffset modelName = builder.CreateString(model.ProducerName);

            Dictionary<int, int> constantIndexes = new Dictionary<int, int>();
            List<int> constantBufferIndexes = new List<int>();
            List<int> constantBufferOffsets = new List<int>();
            List<List<int>> bufferConstantIndexes = new List<List<int>> { new List<int>() };
            List<int> bufferLengths = new List<int> { 0 };
            long totalWeightBytes = 0;

            for (int i = 0; i < model.constants.Count; i++)
            {
                Constant constant = model.constants[i];
                constantIndexes.Add(constant.index, i);
                if (constant.lengthBytes > MaxConstantSize)
                    throw new InvalidOperationException(
                        $"Constant {constant.index} is too large to serialize."
                    );
                if (
                    bufferLengths[^1] > 0
                    && (long)bufferLengths[^1] + constant.lengthBytes > TargetSegmentSize
                )
                {
                    bufferConstantIndexes.Add(new List<int>());
                    bufferLengths.Add(0);
                }

                int bufferIndex = bufferConstantIndexes.Count - 1;
                int offset = bufferLengths[bufferIndex];
                constantBufferIndexes.Add(bufferIndex);
                constantBufferOffsets.Add(offset);
                bufferConstantIndexes[bufferIndex].Add(i);
                bufferLengths[bufferIndex] = offset + constant.lengthBytes;
                totalWeightBytes += constant.lengthBytes;
            }

            Dictionary<string, int> operatorNames = new Dictionary<string, int>();
            List<Offset<Operator>> operators = new List<Offset<Operator>>();
            List<Offset<Chain>> chains = new List<Offset<Chain>>();
            List<int> cpuChains = new List<int>();

            for (int i = 0; i < model.layers.Count; i++)
            {
                Layer layer = model.layers[i];
                if (!operatorNames.ContainsKey(layer.opName))
                {
                    StringOffset name = builder.CreateString(layer.opName);
                    operators.Add(Operator.CreateOperator(builder, name));
                    operatorNames[layer.opName] = operatorNames.Count;
                }

                int[] layerInputs = new int[layer.inputs.Length];
                for (int j = 0; j < layer.inputs.Length; j++)
                {
                    int input = layer.inputs[j];
                    if (input != -1 && !indexMapping.ContainsKey(input))
                    {
                        int constantIndex = constantIndexes[input];
                        Constant constant = model.constants[constantIndex];
                        VectorOffset size = SentisFlatBuffer.Tensor.CreateFixedSizesVector(
                            builder,
                            constant.shape.ToArray()
                        );
                        Offset<SentisFlatBuffer.Tensor> value =
                            SentisFlatBuffer.Tensor.CreateTensor(
                                builder,
                                (ScalarType)constant.dataType,
                                constant.lengthBytes,
                                size,
                                (uint)(constantBufferIndexes[constantIndex] + 1),
                                constantBufferOffsets[constantIndex]
                            );
                        values.Add(EValue.CreateEValue(builder, KernelTypes.Tensor, value.Value));
                        indexMapping[input] = values.Count - 1;
                    }
                    layerInputs[j] = input == -1 ? -1 : indexMapping[input];
                }

                int previousValueCount = values.Count;
                layer.SerializeFields(builder, values);
                int[] attributeInputs = new int[values.Count - previousValueCount];
                for (int j = 0; j < attributeInputs.Length; j++)
                    attributeInputs[j] = previousValueCount + j;

                List<int> layerOutputs = new List<int>();
                foreach (int output in layer.outputs)
                {
                    ScalarType scalarType = ScalarType.FLOAT;
                    int byteLength = 0;
                    VectorOffset fixedSizes = default;
                    TensorShapeDynamism shapeDynamism = TensorShapeDynamism.STATIC;
                    VectorOffset dynamicSizes = default;
                    bool hasDynamicRank = false;

                    DataType? dataType = model.GetDataType(output);
                    if (dataType.HasValue)
                        scalarType = (ScalarType)dataType.Value;
                    DynamicTensorShape? outputShape = model.GetShape(output);
                    if (outputShape.HasValue)
                    {
                        shapeDynamism = outputShape.Value.IsStatic()
                            ? TensorShapeDynamism.STATIC
                            : TensorShapeDynamism.DYNAMIC_UNBOUND;
                        if (shapeDynamism == TensorShapeDynamism.STATIC)
                        {
                            TensorShape shape = outputShape.Value.ToTensorShape();
                            fixedSizes = SentisFlatBuffer.Tensor.CreateFixedSizesVector(
                                builder,
                                shape.ToArray()
                            );
                            byteLength =
                                shape.length
                                * AllocatorUtils.LengthInBytesPadded(
                                    shape.length,
                                    (DataType)scalarType
                                );
                        }
                        else
                        {
                            dynamicSizes = CreateDynamicSizes(builder, outputShape.Value);
                            hasDynamicRank = outputShape.Value.isRankDynamic;
                        }
                    }

                    Offset<SentisFlatBuffer.Tensor> value = SentisFlatBuffer.Tensor.CreateTensor(
                        builder: builder,
                        scalar_type: scalarType,
                        length_byte: byteLength,
                        fixed_sizesOffset: fixedSizes,
                        shape_dynamism: shapeDynamism,
                        dynamic_sizesOffset: dynamicSizes,
                        has_dynamic_rank: hasDynamicRank
                    );
                    values.Add(EValue.CreateEValue(builder, KernelTypes.Tensor, value.Value));
                    layerOutputs.Add(values.Count - 1);
                    indexMapping[output] = values.Count - 1;
                }

                VectorOffset inputVector = ExecutionPlan.CreateInputsVector(builder, layerInputs);
                VectorOffset outputVector = ExecutionPlan.CreateOutputsVector(
                    builder,
                    layerOutputs.ToArray()
                );
                VectorOffset attributeVector = ExecutionPlan.CreateInputsVector(
                    builder,
                    attributeInputs
                );
                Offset<KernelCall> kernelCall = KernelCall.CreateKernelCall(
                    builder,
                    operatorNames[layer.opName],
                    attributeVector
                );
                Instruction.StartInstruction(builder);
                Instruction.AddInstrArgsType(builder, InstructionArguments.KernelCall);
                Instruction.AddInstrArgs(builder, kernelCall.Value);
                Offset<Instruction> instruction = Instruction.EndInstruction(builder);
                VectorOffset instructionVector = Chain.CreateInstructionsVector(
                    builder,
                    new[] { instruction }
                );
                Chain.StartChain(builder);
                Chain.AddInputs(builder, inputVector);
                Chain.AddOutputs(builder, outputVector);
                Chain.AddInstructions(builder, instructionVector);
                chains.Add(Chain.EndChain(builder));
            }

            for (int i = 0; i < model.outputs.Count; i++)
            {
                Model.Output output = model.outputs[i];
                if (!constantIndexes.TryGetValue(output.index, out int constantIndex))
                    continue;
                Constant constant = model.constants[constantIndex];
                VectorOffset size = SentisFlatBuffer.Tensor.CreateFixedSizesVector(
                    builder,
                    constant.shape.ToArray()
                );
                Offset<SentisFlatBuffer.Tensor> value = SentisFlatBuffer.Tensor.CreateTensor(
                    builder,
                    (ScalarType)constant.dataType,
                    constant.lengthBytes,
                    size,
                    (uint)(constantBufferIndexes[constantIndex] + 1),
                    constantBufferOffsets[constantIndex]
                );
                values.Add(EValue.CreateEValue(builder, KernelTypes.Tensor, value.Value));
                VectorOffset constantVector = ExecutionPlan.CreateInputsVector(
                    builder,
                    new[] { values.Count - 1 }
                );
                KernelCall.CreateKernelCall(builder);
                Instruction.StartInstruction(builder);
                Instruction.AddInstrArgsType(builder, InstructionArguments.NONE);
                Offset<Instruction> instruction = Instruction.EndInstruction(builder);
                VectorOffset instructionVector = Chain.CreateInstructionsVector(
                    builder,
                    new[] { instruction }
                );
                Chain.StartChain(builder);
                Chain.AddInputs(builder, constantVector);
                Chain.AddInstructions(builder, instructionVector);
                chains.Add(Chain.EndChain(builder));
                indexMapping[constant.index] = values.Count - 1;
            }

            List<int> outputIndices = new List<int>();
            List<StringOffset> outputNames = new List<StringOffset>();
            foreach (Model.Output output in model.outputs)
            {
                outputIndices.Add(indexMapping[output.index]);
                outputNames.Add(builder.CreateString(output.name));
            }

            StringOffset[] symbolicNames = new StringOffset[model.symbolicDimNames?.Length ?? 0];
            for (int i = 0; i < symbolicNames.Length; i++)
                symbolicNames[i] = builder.CreateString(model.symbolicDimNames[i]);

            VectorOffset planOutputNames = ExecutionPlan.CreateOutputsNameVector(
                builder,
                outputNames.ToArray()
            );
            VectorOffset planOutputs = ExecutionPlan.CreateOutputsVector(
                builder,
                outputIndices.ToArray()
            );
            VectorOffset planValues = ExecutionPlan.CreateValuesVector(builder, values.ToArray());
            VectorOffset planOperators = ExecutionPlan.CreateOperatorsVector(
                builder,
                operators.ToArray()
            );
            VectorOffset planChains = ExecutionPlan.CreateChainsVector(builder, chains.ToArray());
            VectorOffset cpuChainVector = BackendPartitioning.CreateChainsVector(
                builder,
                cpuChains.ToArray()
            );
            Offset<BackendPartitioning> partitioning =
                BackendPartitioning.CreateBackendPartitioning(
                    builder,
                    cpuChainVector,
                    SentisFlatBuffer.BackendType.CPU
                );
            VectorOffset planSymbolicNames = ExecutionPlan.CreateSymbolicDimNamesVector(
                builder,
                symbolicNames
            );

            ExecutionPlan.StartExecutionPlan(builder);
            ExecutionPlan.AddName(builder, modelName);
            ExecutionPlan.AddInputs(builder, planInputs);
            ExecutionPlan.AddInputsName(builder, planInputNames);
            ExecutionPlan.AddOutputs(builder, planOutputs);
            ExecutionPlan.AddOutputsName(builder, planOutputNames);
            ExecutionPlan.AddValues(builder, planValues);
            ExecutionPlan.AddOperators(builder, planOperators);
            ExecutionPlan.AddChains(builder, planChains);
            ExecutionPlan.AddBackendPartitioning(builder, partitioning);
            ExecutionPlan.AddSymbolicDimNames(builder, planSymbolicNames);
            Offset<ExecutionPlan> executionPlan = ExecutionPlan.EndExecutionPlan(builder);

            Offset<DataSegment>[] segments = new Offset<DataSegment>[bufferLengths.Count];
            for (int i = 0; i < bufferLengths.Count; i++)
                segments[i] = DataSegment.CreateDataSegment(
                    builder,
                    (ulong)totalWeightBytes,
                    (ulong)bufferLengths[i]
                );
            VectorOffset segmentVector = Program.CreateSegmentsVector(builder, segments);

            Program.StartProgram(builder);
            Program.AddVersion(builder, FormatVersion);
            Program.AddExecutionPlan(builder, executionPlan);
            Program.AddSegments(builder, segmentVector);
            Offset<Program> program = Program.EndProgram(builder);
            builder.FinishSizePrefixed(program.Value);

            ReadOnlyMemory<byte> descriptionMemory = builder.DataBuffer.ToReadOnlyMemory(
                builder.DataBuffer.Position,
                builder.DataBuffer.Length - builder.DataBuffer.Position
            );
            if (!MemoryMarshal.TryGetArray(descriptionMemory, out ArraySegment<byte> description))
                throw new InvalidOperationException(
                    "Sentis description buffer is not array-backed."
                );
            stream.Write(description.Array, description.Offset, description.Count);

            for (int bufferIndex = 0; bufferIndex < bufferLengths.Count; bufferIndex++)
            {
                byte[] constantData = new byte[bufferLengths[bufferIndex]];
                foreach (int constantIndex in bufferConstantIndexes[bufferIndex])
                {
                    Constant constant = model.constants[constantIndex];
                    System.Buffer.BlockCopy(
                        constant.array.Array,
                        constant.array.Offset,
                        constantData,
                        constantBufferOffsets[constantIndex],
                        constant.array.Count
                    );
                }

                builder = new FlatBufferBuilder(WeightsFlatBufferOverhead + constantData.Length);
                VectorOffset storage = SentisFlatBuffer.Buffer.CreateStorageVectorBlock(
                    builder,
                    constantData
                );
                SentisFlatBuffer.Buffer.StartBuffer(builder);
                SentisFlatBuffer.Buffer.AddStorage(builder, storage);
                Offset<SentisFlatBuffer.Buffer> weightBuffer = SentisFlatBuffer.Buffer.EndBuffer(
                    builder
                );
                builder.FinishSizePrefixed(weightBuffer.Value);

                ReadOnlyMemory<byte> weightMemory = builder.DataBuffer.ToReadOnlyMemory(
                    builder.DataBuffer.Position,
                    builder.DataBuffer.Length - builder.DataBuffer.Position
                );
                if (!MemoryMarshal.TryGetArray(weightMemory, out ArraySegment<byte> bytes))
                    throw new InvalidOperationException(
                        "Sentis weight buffer is not array-backed."
                    );
                stream.Write(bytes.Array, bytes.Offset, bytes.Count);
                stream.Flush();
            }
        }
    }
}
