cmake_minimum_required(VERSION 3.20)
project(ShitDesignerHapNative C)

add_library(shitdesigner_hap SHARED shitdesigner_hap.c)
target_compile_features(shitdesigner_hap PRIVATE c_std_11)
target_include_directories(shitdesigner_hap PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
if (APPLE)
  find_package(Threads REQUIRED)
  target_link_libraries(shitdesigner_hap PRIVATE Threads::Threads m)
elseif (UNIX)
  find_package(Threads REQUIRED)
  target_link_libraries(shitdesigner_hap PRIVATE Threads::Threads m)
endif()

include(CTest)
if (BUILD_TESTING)
  add_executable(hap_native_smoke hap_native_smoke.c shitdesigner_hap.c)
  target_compile_features(hap_native_smoke PRIVATE c_std_11)
  target_include_directories(hap_native_smoke PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
  if (APPLE OR UNIX)
    target_link_libraries(hap_native_smoke PRIVATE Threads::Threads m)
  endif()
  add_test(NAME HapNativeFixtures COMMAND hap_native_smoke
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../ShitDesigner/Tests/Media/Fixtures/hap1.mov
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../ShitDesigner/Tests/Media/Fixtures/hap5.mov
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../ShitDesigner/Tests/Media/Fixtures/hapy.mov
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../ShitDesigner/Tests/Media/Fixtures/hapm.mov
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../ShitDesigner/Tests/Media/Fixtures/hap1-snappy-multichunk.mov
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../ShitDesigner/Tests/Media/Fixtures/malformed-truncated.mov
    ${CMAKE_CURRENT_SOURCE_DIR}/../../../ShitDesigner/Tests/Media/Fixtures/malformed-stsc.mov)
endif()

if (WIN32)
  set_target_properties(shitdesigner_hap PROPERTIES OUTPUT_NAME "shitdesigner_hap")
endif()

# The decoder is self-contained: the checked-in C implementation contains the
# bounded MOV sample-table reader, raw Snappy decoder, Hap section decoder and
# CPU BC1/BC3/BC4 readback path. GPU upload is a host concern and consumes the
# same frame ABI. No third-party binary is downloaded by this build.
