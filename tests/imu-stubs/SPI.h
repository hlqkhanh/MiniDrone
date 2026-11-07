#pragma once
#include <cstdint>
struct SPISettings { SPISettings(int,int,int){} };
struct SpiStub {
 void begin(int,int,int,int){}
 void beginTransaction(SPISettings){}
 void endTransaction(){}
 uint8_t transfer(uint8_t){return 0;}
};
inline SpiStub SPI;
