function y = controller_block(command,feedback,c,mode)
%#codegen
persistent state stopped faultCode
if isempty(state)
 state=zeros(1,7);stopped=false;faultCode=0;
end
if stopped
 y=zeros(1,14);y(14)=faultCode;return
end
[y,state]=roll_step(state,c,mode,command,feedback(1),feedback(2),.005);
if y(14)~=0
 stopped=true;faultCode=y(14);
end
end
