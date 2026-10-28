#pragma once
namespace hw {
constexpr int sck=18,miso=19,mosi=23,cs=5;
constexpr int motors[4]={14,15,12,13}; // LF RF LR RR
constexpr const char* version="DroneRebuild-0.2";
constexpr const char* profile="XW_Z1_SPI_ROLL_PITCH_V2";
// Right-handed mounting map: body X=-sensor Y, body Y=sensor X,
// body Z=sensor Z. Confirm these axes on the physical frame before ARM.
constexpr double rollSensorSign=-1;
constexpr double beta=.075;
constexpr unsigned baud=460800;
}
