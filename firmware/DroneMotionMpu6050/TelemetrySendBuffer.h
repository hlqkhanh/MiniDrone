#pragma once
#include <stddef.h>
#include <stdint.h>

// Keep partially sent lines intact. Under sustained congestion drop NEW whole
// lines rather than corrupting the stream or blocking sensor sampling.
template <size_t Capacity>
class TelemetrySendBuffer {
 public:
  struct SendResult { int bytes; bool retryable; };
  enum class FlushResult { Idle, Progress, Pending, Failed, Stalled };

  constexpr size_t size() const { return end_ - begin_; }
  constexpr void clear() { begin_ = end_ = 0; }

  constexpr bool append(const uint8_t *line, size_t length, uint32_t now) {
    if (length > Capacity - size()) return false;
    if (!size()) lastProgress_ = now;
    if (length > Capacity - end_) {
      const size_t remaining = size();
      for (size_t i = 0; i < remaining; ++i) bytes_[i] = bytes_[begin_ + i];
      begin_ = 0;
      end_ = remaining;
    }
    for (size_t i = 0; i < length; ++i) bytes_[end_++] = line[i];
    return true;
  }

  template <typename Sender>
  constexpr FlushResult flush(uint32_t now, Sender sender) {
    if (!size()) return FlushResult::Idle;
    const SendResult result = sender(bytes_ + begin_, size());
    if (result.bytes > 0 && static_cast<size_t>(result.bytes) <= size()) {
      begin_ += result.bytes;
      if (begin_ == end_) clear();
      lastProgress_ = now;
      return FlushResult::Progress;
    }
    if (result.bytes < 0 && !result.retryable) return FlushResult::Failed;
    // Wrap-safe timeout; transient EAGAIN and partial sends keep the connection.
    return static_cast<uint32_t>(now - lastProgress_) >= 20000
      ? FlushResult::Stalled : FlushResult::Pending;
  }

 private:
  uint8_t bytes_[Capacity]{};
  size_t begin_ = 0, end_ = 0;
  uint32_t lastProgress_ = 0;
};
