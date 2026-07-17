using System;
using UnityEngine;

namespace Hypocycloid.Utils
{
    public static class LayerHelper
    {
        public static int GetLastLayer(LayerMask layer) => (int)Mathf.Log(layer.value, 2);
    }
}
