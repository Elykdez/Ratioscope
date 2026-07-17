using Unity.InferenceEngine;

namespace Hypocycloid.Utils
{
    // Sentis/Inference Engine model. The runtime loads it with ModelLoader.Load(ModelAsset);
    // keep UseInstance off (instancing a ModelAsset is meaningless).
    public sealed class AssetLoadModel : AssetLoadBase<ModelAsset> { }
}
