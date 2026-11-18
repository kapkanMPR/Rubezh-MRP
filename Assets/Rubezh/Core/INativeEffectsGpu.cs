namespace SilentOrders.Rendering
{
    // Presentation ownership only; simulation remains independent of Unity.
    public interface INativeEffectsGpu { bool OwnsBattleEffects { get; } }
}
