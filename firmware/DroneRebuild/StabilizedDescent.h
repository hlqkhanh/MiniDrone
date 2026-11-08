#pragma once
#include "RollCore.h"

namespace rebuild
{
    // Open-loop collective reduction, NOT an altitude/landing controller.
    // The existing dual-axis ANGLE controller must keep running throughout.
    class StabilizedDescent
    {
        bool active_ = false, paused_ = false;
        double collective_ = 0, ceiling_ = 0, floor_ = 0, rate_ = 0;
        double rollTarget_ = 0, pitchTarget_ = 0;

        static double towardZero(double angle, double amount)
        {
            return angle > 0 ? std::fmax(0., angle - amount)
                             : std::fmin(0., angle + amount);
        }

      public:
        void reset() { *this = StabilizedDescent{}; }
        bool active() const { return active_; }
        bool paused() const { return paused_; }
        bool atFloor() const { return active_ && collective_ <= floor_; }
        double collective() const { return collective_; }
        double floor() const { return floor_; }
        double rate() const { return rate_; }
        double rollTarget() const { return rollTarget_; }
        double pitchTarget() const { return pitchTarget_; }
        const char* reason() const
        {
            return !active_ ? "NONE" : atFloor() ? "DESCENT_FLOOR"
                                  : paused_ ? "DESCENT_PAUSED" : "DESCENDING";
        }

        bool ownedBy(const Session& s, uint32_t owner, uint32_t epoch) const
        {
            return active_ && s.state == State::Running && owner == s.owner && epoch == s.epoch;
        }

        bool begin(const Session& s, uint32_t owner, uint32_t epoch,
                   const Config& roll, const Config& pitch, Mode mode,
                   bool both, bool bench, double rt, double pt,
                   double reductionPerSecond, double minimum)
        {
            if (active_ || s.state != State::Running || s.owner != owner || s.epoch != epoch ||
                owner == 0 || !both || bench || mode != Mode::Angle ||
                !DualController::compatible(roll, pitch) || roll.kp <= 0 || pitch.kp <= 0 ||
                roll.angleKp <= 0 || pitch.angleKp <= 0 ||
                !std::isfinite(rt) || !std::isfinite(pt) || std::fabs(rt) > 10 || std::fabs(pt) > 10 ||
                !std::isfinite(reductionPerSecond) || reductionPerSecond < .1 || reductionPerSecond > 10 ||
                !std::isfinite(minimum) || minimum < 1 || minimum >= roll.base ||
                // Preserve command headroom for both configured corrections.
                // This is not a measured minimum thrust or a hover guarantee.
                minimum <= roll.outputLimit + pitch.outputLimit)
                return false;
            active_ = true;
            paused_ = false;
            collective_ = ceiling_ = roll.base;
            floor_ = minimum;
            rate_ = reductionPerSecond;
            rollTarget_ = rt;
            pitchTarget_ = pt;
            return true;
        }

        bool pause(const Session& s, uint32_t owner, uint32_t epoch)
        {
            if (!ownedBy(s, owner, epoch)) return false;
            paused_ = true;
            return true;
        }

        bool resume(const Session& s, uint32_t owner, uint32_t epoch)
        {
            if (!ownedBy(s, owner, epoch) || atFloor()) return false;
            paused_ = false;
            return true;
        }

        bool boost(const Session& s, uint32_t owner, uint32_t epoch, double delta)
        {
            if (!ownedBy(s, owner, epoch) || !std::isfinite(delta) || delta <= 0 || delta > 10 ||
                collective_ >= ceiling_)
                return false;
            collective_ = std::fmin(ceiling_, collective_ + delta);
            paused_ = true;
            return true;
        }

        bool step(double dt)
        {
            if (!active_ || !std::isfinite(dt) || dt < .002 || dt > .02) return false;
            // Slew the previous angle setpoints to level without resetting PID I/D.
            rollTarget_ = towardZero(rollTarget_, 5 * dt);
            pitchTarget_ = towardZero(pitchTarget_, 5 * dt);
            if (!paused_) collective_ = std::fmax(floor_, collective_ - rate_ * dt);
            return true;
        }
    };
}
