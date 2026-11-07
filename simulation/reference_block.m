function command = reference_block(t,mode)
%#codegen
command=0;
if mode==0 || mode==2
 if t>=1 && t<1.3
  command=5;
 elseif t>=2 && t<2.3
  command=-5;
 end
end
% Angle mode recovers from plant initial angle to zero.
end
