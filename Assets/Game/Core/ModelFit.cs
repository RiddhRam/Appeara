using UnityEngine;

namespace Armory.Core
{
    /// <summary>
    /// Normalises an imported model to a gameplay size. Downloaded models arrive at wildly different scales and
    /// pivots - centimetres or metres, pivot at the feet or at the centre - so the fit works from measured bounds
    /// alone: scale to the wanted height, then stand it on the deck over its own pivot. Pure, so it is unit-tested.
    /// </summary>
    public readonly struct ModelFit
    {
        public readonly float Scale;
        /// <summary>Local position that puts the model's feet on y = 0 and centres it horizontally.</summary>
        public readonly Vector3 Offset;

        private ModelFit(float scale, Vector3 offset)
        {
            Scale = scale;
            Offset = offset;
        }

        public static ModelFit Solve(Bounds bounds, float targetHeight)
        {
            float height = bounds.size.y;
            // A zero target means "trust the import scale"; a flat or empty mesh would divide the scale to infinity.
            if (targetHeight <= 0f || height <= Mathf.Epsilon) return new ModelFit(1f, Vector3.zero);
            float scale = targetHeight / height;
            return new ModelFit(scale, new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale);
        }
    }
}
