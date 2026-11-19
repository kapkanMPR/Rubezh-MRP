using System;
using System.Collections.Generic;
using SilentOrders.Core;
using SilentOrders.Rendering;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeBattleComposer
    {
        // Immutable observations at simulation boundaries. Interpolation never writes into Core.
        internal struct ActorFrame
        {
            public V2 World,
                Anchor,
                Weapon;
            public float Depth,
                WeaponDepth,
                Angle,
                Bob;
        }

        sealed class ActorFrames
        {
            public Soldier Soldier;
            public bool Alive;
            public ActorFrame Previous,
                Current;
        }

        readonly Dictionary<int, ActorFrames> frames = new Dictionary<int, ActorFrames>();
        readonly HashSet<int> observedIds = new HashSet<int>();
        readonly List<int> removedActorIds = new List<int>();
        public int RosterRevision { get; private set; }
        public int TrackedActorCount => frames.Count;
        public int ActorPruned { get; private set; }
        BattleSimulation observedBattle;
        int observedTick = -1;
        public float InterpolationAlpha { get; private set; } = 1f;
        public bool InterpolationActive { get; private set; }
        public int ActorStatusRebuilds { get; private set; }
        public int ActorTransformUpdates { get; private set; }
        public int ActorPoseUpdates { get; private set; }
        public int InterpolationFrames { get; private set; }

        static V2 Blend(V2 a, V2 b, float t) => a + (b - a) * t;

        internal ActorFrame Sample(Soldier soldier, BattleSimulation battle)
        {
            var world = soldier.Position + soldier.Exchange.Visual;
            var pivot = Project(world, battle.Map);
            var rounded = new V2((float)Math.Round(pivot.X), (float)Math.Round(pivot.Y));
            var anchor = (pivot * .15f + rounded * .85f) * Canvas.Scale;
            WeaponPose(
                battle,
                soldier,
                IsLaunching(battle, soldier),
                out var center,
                out var muzzle,
                out var aim,
                out var weapon,
                out var left
            );
            bool low =
                soldier.Incapacitated
                || (
                    soldier.RescuerId >= 0
                    && soldier.RescuerId < battle.Soldiers.Count
                    && battle.Soldiers[soldier.RescuerId].RescuePhase == RescuePhase.Drag
                );
            return new ActorFrame
            {
                World = world,
                Anchor = anchor,
                Depth = soldier.Position.Y - soldier.Position.X,
                Weapon = (pivot * .15f + center * .85f) * Canvas.Scale - anchor,
                WeaponDepth = SomaliFire.Visual(soldier, battle.Time)
                    ? soldier.SomaliMuzzle.Y - soldier.SomaliMuzzle.X - (soldier.Position.Y - soldier.Position.X) + .02f
                    : 0,
                Angle = aim * 180f / (float)Math.PI,
                Bob =
                    soldier.Alive && !low && soldier.Moving
                        ? (float)Math.Round((float)Math.Sin(battle.Time * 10f + soldier.Id) * .85f)
                            * .85f
                            * Canvas.Scale
                        : 0,
            };
        }

        void ForgetActor(int id)
        {
            if (frames.Remove(id))
                ActorPruned++;
            actors.Remove(id);
            currentRenderer?.RemoveActorVisual(id);
        }

        public void ObserveTick(BattleSimulation battle)
        {
            bool reset = observedBattle != battle;
            if (reset)
            {
                currentRenderer?.ReleaseAllActorVisuals();
                ActorPruned += frames.Count;
                frames.Clear();
                actors.Clear();
                observedBattle = battle;
                observedTick = -1;
            }
            if (battle == null)
            {
                if (reset)
                    RosterRevision++;
                InterpolationActive = false;
                return;
            }
            bool tickChanged = reset || observedTick != battle.TickNumber;
            bool topologyChanged = reset;
            // Reconcile references, not just count or IDs: reinforcements and replacement
            // can happen while paused or between two observations of the same tick.
            observedIds.Clear();
            foreach (var soldier in battle.Soldiers)
            {
                if (soldier == null)
                    continue;
                observedIds.Add(soldier.Id);
                ActorFrames pair;
                bool replacement = !frames.TryGetValue(soldier.Id, out pair) || !ReferenceEquals(pair.Soldier, soldier);
                if (replacement)
                {
                    ForgetActor(soldier.Id);
                    var now = Sample(soldier, battle);
                    frames[soldier.Id] = new ActorFrames
                    {
                        Soldier = soldier,
                        Alive = soldier.Alive,
                        Previous = now,
                        Current = now,
                    };
                    topologyChanged = true;
                }
                else if (tickChanged)
                {
                    var now = Sample(soldier, battle);
                    pair.Previous = pair.Current;
                    pair.Current = now;
                    var delta = now.World - pair.Previous.World;
                    if (delta.X * delta.X + delta.Y * delta.Y > 16f || pair.Alive != soldier.Alive)
                        pair.Previous = now;
                    pair.Alive = soldier.Alive;
                }
            }
            removedActorIds.Clear();
            foreach (var id in frames.Keys)
                if (!observedIds.Contains(id))
                    removedActorIds.Add(id);
            foreach (var id in removedActorIds)
            {
                ForgetActor(id);
                topologyChanged = true;
            }
            if (topologyChanged)
                RosterRevision++;
            InterpolationActive = false;
            foreach (var pair in frames.Values)
                if (pair.Soldier.Deployed && battle.Fog.CanDraw(pair.Soldier) && Different(pair.Previous, pair.Current))
                    InterpolationActive = true;
            observedTick = battle.TickNumber;
        }

        internal bool UsesRenderer(HdGpuRenderer renderer) => ReferenceEquals(currentRenderer, renderer);

        internal void ForgetRenderer(HdGpuRenderer renderer)
        {
            if (!UsesRenderer(renderer))
                return;
            renderer.ReleaseAllActorVisuals();
            actors.Clear();
            currentRenderer = null;
        }

        ActorFrame EnsureFrame(BattleSimulation battle, Soldier soldier)
        {
            ActorFrames pair;
            if (frames.TryGetValue(soldier.Id, out pair) && ReferenceEquals(pair.Soldier, soldier))
                return pair.Current;
            ForgetActor(soldier.Id);
            var now = Sample(soldier, battle);
            frames[soldier.Id] = new ActorFrames
            {
                Soldier = soldier,
                Alive = soldier.Alive,
                Previous = now,
                Current = now,
            };
            RosterRevision++;
            return now;
        }

        static bool Different(ActorFrame a, ActorFrame b) =>
            a.Anchor.X != b.Anchor.X
            || a.Anchor.Y != b.Anchor.Y
            || a.Angle != b.Angle
            || a.Bob != b.Bob
            || a.Weapon.X != b.Weapon.X
            || a.Weapon.Y != b.Weapon.Y
            || a.Depth != b.Depth
            || a.WeaponDepth != b.WeaponDepth;

        public void SetInterpolation(float alpha)
        {
            InterpolationAlpha = Math.Max(0, Math.Min(1, alpha));
        }

        public V2 VisualPosition(Soldier soldier)
        {
            ActorFrames pair;
            return frames.TryGetValue(soldier.Id, out pair) && ReferenceEquals(pair.Soldier, soldier)
                ? Blend(pair.Previous.World, pair.Current.World, InterpolationAlpha)
                : soldier.Position + soldier.Exchange.Visual;
        }

        internal void ApplyActorTransforms(HdGpuRenderer renderer)
        {
            foreach (var pair in frames.Values)
            {
                var a = pair.Previous;
                var b = pair.Current;
                float t = InterpolationAlpha;
                var f = b;
                f.Anchor = Blend(a.Anchor, b.Anchor, t);
                f.World = Blend(a.World, b.World, t);
                f.Weapon = Blend(a.Weapon, b.Weapon, t);
                f.Bob = a.Bob + (b.Bob - a.Bob) * t;
                f.Depth = a.Depth + (b.Depth - a.Depth) * t;
                f.WeaponDepth = a.WeaponDepth + (b.WeaponDepth - a.WeaponDepth) * t;
                float delta = (b.Angle - a.Angle) % 360f;
                if (delta > 180)
                    delta -= 360;
                if (delta < -180)
                    delta += 360;
                f.Angle = a.Angle + delta * t;
                bool moved,
                    posed;
                renderer.UpdateActorTransform(pair.Soldier.Id, f, out moved, out posed);
                if (moved)
                    ActorTransformUpdates++;
                if (posed)
                    ActorPoseUpdates++;
            }
            if (InterpolationActive)
                InterpolationFrames++;
        }

        // Exact displayed values only: health changes inside the same pixel-wide bar do no work.
        int StatusKey(BattleSimulation b, Soldier s, bool selected)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (s.Alive ? 1 : 0);
                h = h * 31 + (s.Incapacitated ? 1 : 0);
                h = h * 31 + (int)(s.Health * .12f);
                h = h * 31 + (s.Suppression > .45f ? (int)(s.Suppression * 12) + 1 : 0);
                h = h * 31 + (s.SupportReady ? 1 : 0);
                h = h * 31 + (s.Broken ? 1 : 0);
                h = h * 31 + (int)s.State;
                h = h * 31 + (selected ? 1 : 0);
                h = h * 31 + (s.Crouched ? 1 : 0);
                h =
                    h * 31
                    + (
                        s.RescuerId >= 0
                        && s.RescuerId < b.Soldiers.Count
                        && b.Soldiers[s.RescuerId].RescuePhase == RescuePhase.Drag
                            ? 1
                            : 0
                    );
                h = h * 31 + (int)s.Side;
                bool leader = b.Team(s).LeaderId == s.Id;
                h = h * 31 + (leader ? 1 : 0);
                if (leader || s.State == ActionState.Reload)
                    h = h * 31 + (int)(EnsureFrame(b, s).Bob * 100);
                return h;
            }
        }

        void DrawSection(
            BattleSimulation b,
            Soldier s,
            HdGpuRenderer renderer,
            int section,
            bool changed,
            bool selected
        )
        {
            int group = s.Id * 4 + section;
            if (!changed)
            {
                renderer.ReuseDirectActor(group);
                return;
            }
            if (section == 2)
                ActorStatusRebuilds++;
            var pivot = P(s.Position + s.Exchange.Visual);
            Canvas.SetObjectTransform(pivot.X, pivot.Y, .85f, .85f, 0);
            Canvas.TestDepth = true;
            Canvas.Depth = 256f + s.Position.Y - s.Position.X + .06f;
            Canvas.RetainedObjectGeometry = true;
            SoldierVisualSection = section;
            renderer.BeginVisual(group, pivot.X, pivot.Y);
            try
            {
                DrawSoldierVisual(b, s, selected);
            }
            finally
            {
                renderer.EndVisual();
                Canvas.ClearObjectTransform();
                Canvas.RetainedObjectGeometry = false;
                SoldierVisualSection = -1;
            }
        }

        protected override void BeginHeldWeaponVisual()
        {
            currentRenderer?.SetActorWeaponCapture(true);
        }

        protected override void EndHeldWeaponVisual()
        {
            currentRenderer?.SetActorWeaponCapture(false);
        }
    }
}
