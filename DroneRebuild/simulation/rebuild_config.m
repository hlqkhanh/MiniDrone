function [c,p] = rebuild_config()
% DEMONSTRATION ONLY. Never automatically upload these gains to hardware.
% c: kp ki kd angleKp outputLimit iLimit rateLimit dHz base motorSign
%    maxAngle maxRate saturationSeconds saturationError
c = [0.8 0.05 0.005 2 20 10 20 10 80 1 15 150 0.5 5];
% p: J [kg*m^2], damping [N*m*s/rad], torque/duty [N*m/duty],
% motor time constant [s], plant physical sign, initial angle [deg],
% gyro noise amplitude [deg/s], gyro bias [deg/s], rig spring [N*m/rad]
p = [0.0001 0.00004 0.00002 0.06 1 3 0 0 0];
end
