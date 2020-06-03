namespace UnityEngine.Rendering.CustomRenderPipeline
{
    [CreateAssetMenu]
    public class CustomRenderPipelineAsset : RenderPipelineAsset
    {
        [Range(0,16)]
        public int DebugZBinningIndex = 0;

        [Range(16, 128)] public int TileSize = 64;

        public Texture2D NumberChartTexture;
        public bool DebugTileCount;
        protected override RenderPipeline CreatePipeline()
        {
            return new CustomRenderPipeline(this);
        }
    }
}


