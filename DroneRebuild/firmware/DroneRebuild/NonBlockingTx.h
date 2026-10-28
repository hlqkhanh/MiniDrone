#pragma once
#include <cstddef>
#include <cerrno>

namespace rebuild {
// One bounded socket send per loop. Sender MUST use non-blocking I/O.
// Returns 1=complete, 0=retry later, -1=closed/error. Does not inspect
// Print::availableForWrite(): ESP32 NetworkClient inherits its default 0.
template<class Sender>
int pumpTcp(const char* data,std::size_t length,std::size_t& offset,Sender send) {
  if(offset>=length)return 1;
  const std::size_t left=length-offset;
  const std::size_t chunk=left<256?left:256;
  const int count=send(data+offset,chunk);
  if(count>0) {
    if(static_cast<std::size_t>(count)>chunk)return -1;
    offset+=static_cast<std::size_t>(count);
    return offset==length?1:0;
  }
  if(count<0 && (errno==EAGAIN || errno==EWOULDBLOCK || errno==EINTR))return 0;
  return -1;
}
}
