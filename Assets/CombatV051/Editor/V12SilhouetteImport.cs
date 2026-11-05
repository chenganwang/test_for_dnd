using UnityEditor;

namespace CombatV051.Editor
{
    // Narrow import rule: existing portraits, authored references and all other
    // textures retain their own GUIDs and import settings.
    public sealed class V12SilhouetteImport:AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            const string folder="Assets/CombatV051/Resources/UI/V12Silhouettes/";
            if(assetPath!=folder+"Evan.png"&&assetPath!=folder+"Mira.png"&&assetPath!=folder+"Finn.png")return;
            var texture=(TextureImporter)assetImporter;
            texture.textureType=TextureImporterType.Sprite;texture.spriteImportMode=SpriteImportMode.Single;
            texture.alphaSource=TextureImporterAlphaSource.FromInput;texture.alphaIsTransparency=true;
            texture.mipmapEnabled=false;texture.maxTextureSize=2048;texture.textureCompression=TextureImporterCompression.Uncompressed;
            texture.filterMode=UnityEngine.FilterMode.Bilinear;texture.wrapMode=UnityEngine.TextureWrapMode.Clamp;
            var settings=new TextureImporterSettings();texture.ReadTextureSettings(settings);settings.spriteMeshType=UnityEngine.SpriteMeshType.FullRect;texture.SetTextureSettings(settings);
        }
    }
}
