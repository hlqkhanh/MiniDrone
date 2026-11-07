#include "../firmware/DroneRebuild/NonBlockingTx.h"
#include <cassert>
#include <cstring>
#include <string>
#include <iostream>
int main() {
 const std::string packet="ACK,1,OK,0,INFO\n";std::size_t offset=0;std::string wire;int calls=0;
 // Reproduce a client whose inherited availableForWrite() always returns 0.
 // The fixed pump uses a non-blocking sender instead, including short writes.
 auto sender=[&](const char* bytes,std::size_t count) {
  calls++;if(calls==1){errno=EAGAIN;return -1;}
  const int n=int(count<3?count:3);wire.append(bytes,n);return n;
 };
 assert(rebuild::pumpTcp(packet.data(),packet.size(),offset,sender)==0 && offset==0);
 while(rebuild::pumpTcp(packet.data(),packet.size(),offset,sender)==0){}
 assert(wire==packet && offset==packet.size());
 offset=0;assert(rebuild::pumpTcp(packet.data(),packet.size(),offset,[](const char*,std::size_t){return 0;})==-1);
 assert(rebuild::pumpTcp(packet.data(),packet.size(),offset,[](const char*,std::size_t){errno=ECONNRESET;return -1;})==-1);
 std::string big(700,'x');offset=0;std::size_t maximum=0;
 while(rebuild::pumpTcp(big.data(),big.size(),offset,[&](const char*,std::size_t n){if(n>maximum)maximum=n;return int(n);})==0){}
 assert(offset==700 && maximum<=256);
 std::cout<<"PASS TCP short writes, EAGAIN retries, disconnect, bounded chunks\n";
}
