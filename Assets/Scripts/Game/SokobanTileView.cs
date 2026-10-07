using UnityEngine;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    /// <summary>地块种类：决定实例化哪一个地块预制体。</summary>
    public enum SokobanTileKind
    {
        Floor,
        Wall,
        Goal
    }

    /// <summary>实体种类：箱子或玩家。</summary>
    public enum SokobanEntityKind
    {
        Box,
        Player
    }

    /// <summary>
    /// 地块预制体上的视图组件。
    /// 地块本身是静态的：一个关卡里每个格子用哪一种预制体在生成时确定，之后不再变化。
    /// 组件只暴露"可被手动替换"的渲染挂点，方便在 Inspector 里换贴图、加特效。
    /// 注意：Unity 的 MonoScript 按"类名 == 文件名"绑定，本类必须独占同名文件，
    /// 否则场景/预制体里的脚本引用会序列化成空。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SokobanTileView : MonoBehaviour
    {
        [SerializeField] private SokobanTileKind kind = SokobanTileKind.Floor;
        [SerializeField] private Image background;
        [SerializeField] private Image goalOverlay;
        [SerializeField] private RectTransform fxMount;

        public SokobanTileKind Kind => kind;
        public Image Background => background;
        public Image GoalOverlay => goalOverlay;
        /// <summary>粒子/特效挂点：在预制体上预留一个空子物体，美术可直接往里挂 ParticleSystem。</summary>
        public RectTransform FxMount => fxMount;

        /// <summary>
        /// 无贴图时回退到主题纯色；有贴图时用白色 tint，避免把美术素材染色。
        /// </summary>
        public void ApplyFallbackTint(Color floor, Color wall, Color goal, Color goalMark)
        {
            if (background != null)
            {
                if (background.sprite == null) background.color = kind == SokobanTileKind.Wall ? wall : kind == SokobanTileKind.Goal ? goal : floor;
                else background.color = SokobanBalatroSkin.IsPlayerScene ? kind == SokobanTileKind.Wall ? SokobanBalatroSkin.WallTint : SokobanBalatroSkin.FloorTint : Color.white;
            }
            if (goalOverlay != null)
            {
                if (goalOverlay.sprite == null) goalOverlay.color = goalMark;
                else goalOverlay.color = SokobanBalatroSkin.IsPlayerScene ? SokobanBalatroSkin.GoalTint : Color.white;
            }
        }
    }
}
