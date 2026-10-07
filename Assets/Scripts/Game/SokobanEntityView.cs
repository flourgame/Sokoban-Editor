using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    /// <summary>
    /// 实体预制体（箱子 / 玩家）上的视图组件。
    /// 实体与地块分层：实体容器是地块容器的后兄弟，因此天然渲染在地块之上，
    /// 箱子压在目标点上时目标点自然露出（叠放语义）。
    /// 预留了 Animator、AudioSource 与特效挂点：一旦在预制体上挂了 Animator，
    /// 代码驱动的换帧会自动让位给 Animator，方便美术完全接管动画。
    /// 注意：Unity 的 MonoScript 按"类名 == 文件名"绑定，本类必须独占同名文件。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SokobanEntityView : MonoBehaviour
    {
        [SerializeField] private SokobanEntityKind kind = SokobanEntityKind.Box;
        [SerializeField] private Image body;
        [SerializeField] private RectTransform fxMount;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private Animator animator;

        [Header("箱子贴图")]
        [SerializeField] private Sprite boxSprite;
        [SerializeField] private Sprite boxOnGoalSprite;

        [Header("玩家行走帧（每方向 3 帧，可留空）")]
        [SerializeField] private Sprite[] playerUp = new Sprite[0];
        [SerializeField] private Sprite[] playerDown = new Sprite[0];
        [SerializeField] private Sprite[] playerLeft = new Sprite[0];
        [SerializeField] private Sprite[] playerRight = new Sprite[0];

        [Header("动画")]
        [Tooltip("勾选后由代码按移动进度循环行走帧；若预制体上挂了 Animator 且希望完全由 Animator 驱动，请取消勾选。")]
        [SerializeField] private bool codeDrivenFrames = true;

        private Coroutine moveEffect;
        private Vector2 bodyBasePosition;
        private Vector3 bodyBaseScale = Vector3.one;
        private bool bodyBaseCaptured;
        private bool goalAfterMove;
        private SokobanDirection lastDirection = SokobanDirection.Down;
        private bool lastOnGoal, animatorPaused;
        private float savedAnimatorSpeed;

        public SokobanEntityKind Kind => kind;
        public Image Body => body;
        public RectTransform FxMount => fxMount;
        public AudioSource Audio => audioSource;
        public Animator AnimatorRef => animator;
        public bool IsPlayer => kind == SokobanEntityKind.Player;

        private void Awake()
        {
            EnsureSeparateBody();
            CaptureBodyBase();
        }

        private void OnEnable()
        {
            SokobanAnimationSettings.Changed += ApplyAnimationSetting;
            ApplyAnimationSetting(SokobanAnimationSettings.Enabled);
        }

        private void ApplyAnimationSetting(bool enabled)
        {
            if (animator != null)
            {
                if (!enabled && !animatorPaused) { savedAnimatorSpeed = animator.speed; animator.speed = 0f; animatorPaused = true; }
                else if (enabled && animatorPaused) { animator.speed = savedAnimatorSpeed; animatorPaused = false; }
            }
            if (!enabled)
            {
                ResetMotion();
                if (IsPlayer) ConfigurePlayer(lastDirection, 0f, lastOnGoal);
            }
        }

        // Older prefabs put their Image on the entity root. Grid movement owns
        // that root; presentation must only animate a separate child.
        public void EnsureSeparateBody()
        {
            if (body == null || body.transform != transform) return;
            var original = body;
            var visual = new GameObject("Body", typeof(RectTransform), typeof(Image));
            var rect = visual.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.SetAsFirstSibling();
            body = visual.GetComponent<Image>();
            body.sprite = original.sprite;
            body.color = original.color;
            body.material = original.material;
            body.type = original.type;
            body.preserveAspect = original.preserveAspect;
            body.raycastTarget = false;
            original.enabled = false;
            bodyBaseCaptured = false;
        }

        private void CaptureBodyBase()
        {
            if (bodyBaseCaptured || body == null) return;
            bodyBasePosition = body.rectTransform.anchoredPosition;
            bodyBaseScale = body.rectTransform.localScale;
            bodyBaseCaptured = true;
        }

        /// <summary>切换箱子贴图（是否在目标点上）。</summary>
        public void ConfigureBox(bool onGoal)
        {
            if (body == null) return;
            body.sprite = onGoal ? boxOnGoalSprite : boxSprite;
            body.color = body.sprite != null ? SokobanBalatroSkin.IsPlayerScene ? SokobanBalatroSkin.BoxTint : Color.white : onGoal ? SokobanTheme.BoxOnGoal : SokobanTheme.Box;
        }

        /// <summary>按方向与移动进度(0..1)选择玩家行走帧。</summary>
        public void ConfigurePlayer(SokobanDirection direction, float phase, bool onGoal)
        {
            lastDirection = direction;
            lastOnGoal = onGoal;
            if (!SokobanAnimationSettings.Enabled) phase = 0f;
            if (body == null) return;
            if (codeDrivenFrames && (animator == null || !animator.isActiveAndEnabled))
            {
                var frames = FramesFor(direction);
                if (frames != null && frames.Length > 0)
                {
                    var clamped = Mathf.Clamp01(phase);
                    var index = Mathf.Min(frames.Length - 1, Mathf.FloorToInt(clamped * frames.Length));
                    body.sprite = frames[index];
                }
            }
            body.color = body.sprite != null ? SokobanBalatroSkin.IsPlayerScene ? SokobanBalatroSkin.PlayerTint : Color.white : onGoal ? SokobanTheme.PlayerOnGoal : SokobanTheme.Player;
        }

        private Sprite[] FramesFor(SokobanDirection direction)
        {
            switch (direction)
            {
                case SokobanDirection.Up: return playerUp;
                case SokobanDirection.Down: return playerDown;
                case SokobanDirection.Left: return playerLeft;
                default: return playerRight;
            }
        }

        /// <summary>转发触发器给 Animator（若存在），供美术把推箱/入位/通关接到状态机。</summary>
        public void PlayTrigger(string trigger)
        {
            if (SokobanAnimationSettings.Enabled && animator != null && !string.IsNullOrEmpty(trigger)) animator.SetTrigger(trigger);
        }

        /// <summary>播放一次性音效（若预制体上挂了 AudioSource 且传入了片段）。</summary>
        public void PlayOneShot(AudioClip clip)
        {
            if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
        }

        /// <summary>普通游玩中的轻量 squash/stretch，不改变网格坐标。</summary>
        public void PlayMoveEffect(bool pushed, SokobanDirection direction)
        {
            if (body == null || !SokobanPlayerPresentation.IsEnabled || !SokobanAnimationSettings.Enabled) return;
            CaptureBodyBase();
            if (moveEffect != null) StopCoroutine(moveEffect);
            goalAfterMove = false;
            body.rectTransform.anchoredPosition = bodyBasePosition;
            body.rectTransform.localScale = bodyBaseScale;
            moveEffect = StartCoroutine(AnimateMoveEffect(pushed, direction));
            PlayTrigger(pushed ? "Push" : "Move");
        }

        /// <summary>箱子进入目标时的短回弹。</summary>
        public void PlayGoalEffect()
        {
            if (body == null || !SokobanPlayerPresentation.IsEnabled || !SokobanAnimationSettings.Enabled) return;
            if (moveEffect != null) { goalAfterMove = true; return; }
            CaptureBodyBase();
            if (moveEffect != null) StopCoroutine(moveEffect);
            body.rectTransform.anchoredPosition = bodyBasePosition;
            body.rectTransform.localScale = bodyBaseScale;
            moveEffect = StartCoroutine(AnimateGoalEffect());
            PlayTrigger("Goal");
        }

        private IEnumerator AnimateMoveEffect(bool pushed, SokobanDirection direction)
        {
            var bodyRect = body.rectTransform;
            var baseScale = bodyBaseScale;
            var basePosition = bodyBasePosition;
            var horizontal = direction == SokobanDirection.Left || direction == SokobanDirection.Right;
            var compress = pushed ? (horizontal ? new Vector3(0.84f, 1.08f, 1f) : new Vector3(1.08f, 0.84f, 1f)) : new Vector3(0.96f, 1.04f, 1f);
            var expand = pushed ? (horizontal ? new Vector3(1.05f, 0.96f, 1f) : new Vector3(0.96f, 1.05f, 1f)) : new Vector3(1.02f, 0.98f, 1f);
            var elapsed = 0f;
            while (elapsed < 0.06f)
            {
                elapsed += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(elapsed / 0.06f);
                bodyRect.localScale = Vector3.Scale(baseScale, Vector3.Lerp(Vector3.one, compress, p));
                bodyRect.anchoredPosition = basePosition + new Vector2(Mathf.Sin(p * Mathf.PI * 4f), Mathf.Sin(p * Mathf.PI * 3f)) * (1f - p) * 1.2f;
                yield return null;
            }
            elapsed = 0f;
            while (elapsed < 0.10f)
            {
                elapsed += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(elapsed / 0.10f);
                p = p * p * (3f - 2f * p);
                bodyRect.localScale = Vector3.Scale(baseScale, Vector3.LerpUnclamped(compress, expand, p));
                bodyRect.anchoredPosition = basePosition;
                yield return null;
            }
            elapsed = 0f;
            while (elapsed < 0.08f)
            {
                elapsed += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(elapsed / 0.08f);
                bodyRect.localScale = Vector3.Scale(baseScale, Vector3.LerpUnclamped(expand, Vector3.one, p));
                yield return null;
            }
            bodyRect.localScale = baseScale;
            bodyRect.anchoredPosition = basePosition;
            if (goalAfterMove)
            {
                goalAfterMove = false;
                yield return AnimateGoalEffect();
            }
            moveEffect = null;
        }

        private IEnumerator AnimateGoalEffect()
        {
            var bodyRect = body.rectTransform;
            var baseScale = bodyBaseScale;
            var elapsed = 0f;
            while (elapsed < 0.24f)
            {
                elapsed += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(elapsed / 0.24f);
                var bump = Mathf.Sin(p * Mathf.PI) * 0.10f;
                bodyRect.localScale = baseScale * (1f + bump);
                yield return null;
            }
            bodyRect.localScale = baseScale;
            moveEffect = null;
        }

        public void ResetMotion()
        {
            if (moveEffect != null) StopCoroutine(moveEffect);
            moveEffect = null;
            goalAfterMove = false;
            CaptureBodyBase();
            if (body == null || !bodyBaseCaptured) return;
            body.rectTransform.anchoredPosition = bodyBasePosition;
            body.rectTransform.localScale = bodyBaseScale;
        }

        private void OnDisable()
        {
            SokobanAnimationSettings.Changed -= ApplyAnimationSetting;
            ResetMotion();
            if (animator != null && animatorPaused) animator.speed = savedAnimatorSpeed;
            animatorPaused = false;
        }
    }
}
