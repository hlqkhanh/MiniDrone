#pragma once
#include <cmath>
#include <cstdint>
#include <cstddef>
using std::isfinite;
constexpr int LOW=0,HIGH=1,OUTPUT=1,MSBFIRST=1,SPI_MODE0=0;
inline void digitalWrite(int,int){}
inline void pinMode(int,int){}
inline void delayMicroseconds(int){}
inline void delay(int){}
