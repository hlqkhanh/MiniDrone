function feedback = plant_block(control,p,t,mode)
%#codegen
% Single-axis rig. Physical model is independent of controller motor sign.
% Outputs measured angle [deg], measured gyro roll [deg/s].
% This ideal-angle sensor does NOT validate Madgwick or real IMU mounting.
persistent angle rate torque
if isempty(angle)
 angle=p(6);rate=0;torque=0;
end
differential=(control(8)+control(10)-control(9)-control(11))/4;
if mode==2
 differential=0; % Firmware DRY_RUN never enables the motor gate.
end
wanted=p(5)*p(3)*differential;
for k=1:5
 torque=torque+(wanted-torque)*(.001/p(4));
 accel=(torque-p(2)*rate*pi/180-p(9)*angle*pi/180)/p(1)*180/pi;
 rate=rate+accel*.001;angle=angle+rate*.001;
end
feedback=[angle rate+p(7)*sin(2*pi*37*t)+p(8)];
end
