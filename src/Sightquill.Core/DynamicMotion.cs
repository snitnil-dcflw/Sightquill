namespace Sightquill.Core;
public sealed class DynamicMotion
{
    public double Movement { get; private set; }
    public double Firing { get; private set; }
    public void Reset() { Movement = Firing = 0; }
    public void Step(bool moving, bool firing, double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        var dt = Math.Min(seconds, .1);
        Movement = Approach(Movement, moving ? 1 : 0, dt);
        Firing = Approach(Firing, firing ? 1 : 0, dt);
    }
    private static double Approach(double value, double target, double dt)
    {
        var next = target + (value - target) * Math.Exp(-dt * (target > value ? 22 : 12));
        return Math.Abs(next - target) < .001 ? target : next;
    }
}
