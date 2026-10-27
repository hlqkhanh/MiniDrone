#pragma once
namespace hw {
constexpr int sck=18,miso=19,mosi=23,cs=5;
constexpr int motors[4]={14,15,12,13}; // LF RF LR RR
constexpr const char* version="DroneRebuild-0.1";
constexpr const char* profile="XW_Z1_SPI_ROLL_V1";
// Provisional mounting convention inherited from the old design.
// Physical roll = negative sensor Y Euler angle; body roll rate = -gyroY.
constexpr double rollSensorSign=-1;
constexpr double beta=.075;
constexpr unsigned baud=460800;
}
