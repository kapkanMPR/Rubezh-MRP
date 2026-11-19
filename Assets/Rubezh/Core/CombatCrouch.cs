using System;

namespace SilentOrders.Core
{
    public sealed class CrouchState
    {
        public bool Seated;
        public float StandUntil,
            NextSit;
        public int Sits,
            Rises;
        public double SeatedSeconds,
            AutomaticSeconds;
    }

    public static class CombatCrouch
    {
        public static bool Automatic(Soldier s) =>
            s.Alive && (s.Suppression >= .7f || s.Broken || s.Health <= 35 || s.Incapacitated);

        public static bool Protected(Soldier s) => s.Crouch.Seated || Automatic(s);

        public static float StandDuration(float health) => .5f + .5f * (1 - Util.Clamp(health / 100, 0, 1));

        public static bool Standing(BattleSimulation b, Soldier s) => s.Crouch.StandUntil > b.Time;

        public static void Begin(BattleSimulation b, Soldier s)
        {
            var c = s.Crouch;
            if (Automatic(s))
            {
                c.Seated = false;
                c.StandUntil = 0;
            }
            else if (c.StandUntil > 0 && b.Time >= c.StandUntil)
            {
                c.Seated = false;
                c.StandUntil = 0;
                c.NextSit = b.Time + 2;
                s.Crouched = false;
            }
        }

        public static void Rise(BattleSimulation b, Soldier s)
        {
            var c = s.Crouch;
            if (Automatic(s))
            {
                c.Seated = false;
                c.StandUntil = 0;
                return;
            }

            if (c.Seated && c.StandUntil <= 0)
            {
                c.StandUntil = b.Time + StandDuration(s.Health);
                c.Rises++;
                s.Reason = "Поднимаюсь из приседа";
            }
        }

        public static bool AllowMove(BattleSimulation b, Soldier s)
        {
            Begin(b, s);
            Rise(b, s);
            return !Standing(b, s);
        }

        public static bool FireAllowed(BattleSimulation b, Soldier s, V2 point)
        {
            if (PairExchange.Active(b, s) || Standing(b, s))
                return false;
            // Only a deliberate deep crouch hides the weapon behind nearby cover.
            // Automatic protection does not introduce new movement/weapon incapacitation.
            if (s.Crouch.Seated && CombatTraining.Cover(b, s, s.Position, point) > .5f)
            {
                if (!s.Busy && s.ReloadUntil <= b.Time)
                    Rise(b, s);
                return false;
            }

            return true;
        }

        public static void ForPair(BattleSimulation b, Soldier s)
        {
            if (
                !s.Moving
                && !MotionCombat.HasRoute(s)
                && s.Crouch.StandUntil <= 0
                && b.Time >= s.Crouch.NextSit
                && (!PositionLogic.Protected(b, s, s.Position) || s.ReloadUntil > b.Time || s.Bleeding)
            )
            {
                if (!s.Crouch.Seated)
                    s.Crouch.Sits++;
                s.Crouch.Seated = true;
                s.Crouched = true;
            }
        }

        public static void End(BattleSimulation b, Soldier s, float dt)
        {
            var c = s.Crouch;
            if (FriendlyGrenadeSafety.Active(b, s))
            {
                s.Crouched = true;
                return;
            }

            if (!s.Alive || !s.Deployed)
            {
                c.Seated = false;
                c.StandUntil = 0;
                return;
            }

            if (Automatic(s))
            {
                c.Seated = false;
                c.StandUntil = 0;
                c.AutomaticSeconds += dt;
                s.Crouched = true;
                return;
            }

            bool behind = PositionLogic.Protected(b, s, s.Position);
            bool shelterTask = s.ReloadUntil > b.Time || s.Busy || s.State == ActionState.Treat || s.Bleeding;
            bool rest =
                !s.Moving
                && (!MotionCombat.HasRoute(s) || shelterTask)
                && !PairExchange.Active(b, s)
                && b.Time >= c.NextSit
                && c.StandUntil <= 0;
            if (rest && (!behind || shelterTask) && !c.Seated)
            {
                c.Seated = true;
                c.Sits++;
            }

            if (c.Seated)
            {
                c.SeatedSeconds += dt;
                s.Crouched = true;
            }
            else
                s.Crouched = Standing(b, s);
        }
    }
}
