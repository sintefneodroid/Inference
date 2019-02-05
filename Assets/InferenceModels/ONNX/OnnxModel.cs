#if ONNX
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics.Tensors;
using Microsoft.ML.OnnxRuntime;
using UnityEngine;

namespace ONNX
{
    class OnnxModel:MonoBehaviour{

        private void Start()
        {
            Console.WriteLine("Using API");
            UseApi();
            Console.WriteLine("Done");
        }


        static void UseApi(){
            var modelPath = Path.Combine(Application.dataPath, "squeezenet1.onnx");

            using (var session = new InferenceSession(modelPath))
            {
                var inputMeta = session.InputMetadata;
                var container = new List<NamedOnnxValue>();

                var inputData =
 LoadTensorFromFile("bench.in"); // this is the data for only one input tensor for this model

                foreach (var name in inputMeta.Keys)
                {
                    var tensor = inputData.ToTensor().ToDenseTensor();
                    //var tensor2 = new DenseTensor<float>(inputData, inputMeta[name].Dimensions);
                    container.Add(NamedOnnxValue.CreateFromTensor(name, tensor));
                }

                // Run the inference
                var results = session.Run(container);  // results is an IReadOnlyList<NamedOnnxValue> container

                // dump the results
                foreach (var r in results){
                    Console.WriteLine($"Output for {r.Name}");
                    Console.WriteLine(r.AsTensor<float>().GetArrayString());
                }

            }
        }

        static float[] LoadTensorFromFile(string filename){
            var tensorData = new List<float>();

            using (var inputFile = new StreamReader(filename))             // read data from file
            {
                inputFile.ReadLine(); //skip the input name
                var dataStr =
 inputFile.ReadLine()?.Split(new[] { ',', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);
                tensorData.AddRange(dataStr.Select(float.Parse));
            }

            return tensorData.ToArray();
        }


    }
}
#endif
