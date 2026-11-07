#pragma once
#include <Arduino.h>
#include "PzemReader.h"
class TelemetryClient {
 public:
  TelemetryClient(const char* baseUrl, const char* deviceCode, const String& encodedSecret, const char* rootCa);
  bool publish(const PzemReading& reading);
  bool heartbeat();
 private:
  String baseUrl, deviceCode, encodedSecret;
  const char* rootCa;
  int lastHttpStatus = 0;
  bool signedPost(const char* path, const String& body, int expectedStatus);
  bool decodeSecret(uint8_t* output, size_t& outputLength) const;
  bool createSignature(const String& method, const String& path, const String& timestamp,
                       const String& nonce, const String& body, String& signature) const;
  String createNonce() const;
  String createSampleId() const;
  bool enqueue(const String& payload);
  bool flushQueue();
};