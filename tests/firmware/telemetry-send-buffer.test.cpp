// Compile-only regression tests: constexpr assertions run in the C++ compiler,
// so they can also be checked with the installed ESP32 cross compiler.
#include "../../firmware/DroneMotionMpu6050/TelemetrySendBuffer.h"

using Buffer = TelemetrySendBuffer<16>;
using State = Buffer::FlushResult;
constexpr uint8_t first[] = {'Q', ',', '1', '\n'};
constexpr uint8_t second[] = {'Q', ',', '2', '\n'};

constexpr bool partialSendThenCongestionPreservesStream() {
  Buffer buffer;
  if (!buffer.append(first, sizeof(first), 0)) return false;
  uint8_t received[8]{};
  size_t used = 0;
  const auto partial = [&received, &used](const uint8_t *data, size_t) {
    received[used++] = data[0]; received[used++] = data[1];
    return Buffer::SendResult{2, false};
  };
  if (buffer.flush(1, partial) != State::Progress || buffer.size() != 2) return false;
  const auto busy = [](const uint8_t *, size_t) { return Buffer::SendResult{-1, true}; };
  if (buffer.flush(10, busy) != State::Pending || buffer.size() != 2) return false;
  if (!buffer.append(second, sizeof(second), 11)) return false;
  const auto sendAll = [&received, &used](const uint8_t *data, size_t length) {
    for (size_t i = 0; i < length; ++i) received[used++] = data[i];
    return Buffer::SendResult{static_cast<int>(length), false};
  };
  if (buffer.flush(12, sendAll) != State::Progress || buffer.size()) return false;
  if (used != 8) return false;
  for (size_t i = 0; i < 4; ++i)
    if (received[i] != first[i] || received[i + 4] != second[i]) return false;
  return true;
}

constexpr bool overflowDropsWholeLinesAndCompactsUnsentBytes() {
  Buffer buffer;
  for (int i = 0; i < 4; ++i) if (!buffer.append(first, 4, 0)) return false;
  if (buffer.append(second, 4, 0) || buffer.size() != 16) return false;
  const auto partial = [](const uint8_t *, size_t) { return Buffer::SendResult{6, false}; };
  buffer.flush(1, partial);
  if (!buffer.append(second, 4, 2) || buffer.size() != 14) return false;
  bool valid = true;
  buffer.flush(3, [&valid](const uint8_t *data, size_t length) {
    for (size_t i = 0; i < length; ++i) {
      const uint8_t expected = i < 10 ? first[(i + 6) % 4] : second[i - 10];
      if (data[i] != expected) valid = false;
    }
    return Buffer::SendResult{static_cast<int>(length), false};
  });
  return valid && buffer.size() == 0;
}

constexpr bool errorsTimeoutAndReset() {
  Buffer buffer;
  const auto busy = [](const uint8_t *, size_t) { return Buffer::SendResult{-1, true}; };
  buffer.append(first, 4, 100);
  if (buffer.flush(20099, busy) != State::Pending) return false;
  // A partial send resets the no-progress timeout.
  buffer.flush(20099, [](const uint8_t *, size_t) { return Buffer::SendResult{1, false}; });
  if (buffer.flush(20100, busy) != State::Pending) return false;
  if (buffer.flush(40099, busy) != State::Stalled) return false;
  buffer.clear();
  if (buffer.flush(40100, busy) != State::Idle) return false;
  buffer.append(second, 4, 40101);
  if (buffer.flush(40102, [](const uint8_t *, size_t) { return Buffer::SendResult{-1, false}; }) != State::Failed) return false;
  buffer.clear();
  buffer.append(first, 4, 0xfffffff0u);
  return buffer.flush(0x10u, busy) == State::Pending &&
         buffer.flush(0x4e10u, busy) == State::Stalled;
}

static_assert(partialSendThenCongestionPreservesStream(), "Partial writes and retryable errors must not disconnect or corrupt lines");
static_assert(overflowDropsWholeLinesAndCompactsUnsentBytes(), "Overflow must drop a complete new line and retain the unsent stream");
static_assert(errorsTimeoutAndReset(), "Only fatal errors or 20 seconds without progress should close the sender");
