using UnityEngine;
using UnityEngine.UI;

namespace Hypocycloid.UI
{
    [ExecuteAlways]
    public class UIMatPropAnim : MonoBehaviour
    {
        [SerializeField]
        Graphic uiMat;

        [SerializeField]
        string property_float;

        [SerializeField]
        float animatedFloat;

        Graphic graphic;
        Material sharedMat;
        Material instanceMat;
        float customFloatActualValue;

        void OnEnable()
        {
            graphic = uiMat == null ? GetComponent<Graphic>() : uiMat;
            if (graphic == null)
                return;

            // Animate a private instance instead of the Graphic's shared material. The getter
            // returns the shared asset, so writing to it mutates every Graphic using it and,
            // under [ExecuteAlways], persists the change into the asset during edit mode.
            sharedMat = graphic.material;
            if (sharedMat == null || !sharedMat.HasProperty(property_float))
                return;

            instanceMat = new Material(sharedMat);
            graphic.material = instanceMat;
            customFloatActualValue = instanceMat.GetFloat(property_float);
        }

        void OnDisable()
        {
            if (graphic != null && graphic.material == instanceMat)
                graphic.material = sharedMat;
            DestroyInstance();
        }

        void LateUpdate()
        {
            if (instanceMat == null)
                return;

            if (animatedFloat != customFloatActualValue)
            {
                customFloatActualValue = animatedFloat;
                instanceMat.SetFloat(property_float, customFloatActualValue);
            }
        }

        void DestroyInstance()
        {
            if (instanceMat == null)
                return;

            if (Application.isPlaying)
                Destroy(instanceMat);
            else
                DestroyImmediate(instanceMat);
            instanceMat = null;
        }
    }
}
