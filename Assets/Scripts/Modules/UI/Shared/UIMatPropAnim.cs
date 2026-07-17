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

        Material canvasMat;
        float customFloatActualValue;

        void OnEnable()
        {
            canvasMat = (uiMat == null ? GetComponent<Graphic>() : uiMat).material;
            customFloatActualValue = canvasMat.GetFloat(property_float);
        }

        void LateUpdate()
        {
            if (animatedFloat != customFloatActualValue)
            {
                customFloatActualValue = animatedFloat;
                canvasMat.SetFloat(property_float, customFloatActualValue);
            }
        }
    }
}
