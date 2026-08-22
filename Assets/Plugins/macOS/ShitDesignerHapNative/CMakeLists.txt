cmake_minimum_required(VERSION 3.20)
project(ShitDesignerHapNativeMac C)

set(CMAKE_OSX_ARCHITECTURES arm64 CACHE STRING "Unity macOS Apple Silicon architecture")
add_library(shitdesigner_hap SHARED
  ../../x86_64/ShitDesignerHapNative/shitdesigner_hap.c)
target_include_directories(shitdesigner_hap PRIVATE
  ../../x86_64/ShitDesignerHapNative)
target_compile_features(shitdesigner_hap PRIVATE c_std_11)
find_package(Threads REQUIRED)
target_link_libraries(shitdesigner_hap PRIVATE Threads::Threads m)
set_target_properties(shitdesigner_hap PROPERTIES
  OUTPUT_NAME "shitdesigner_hap"
  PREFIX ""
  SUFFIX ".dylib")
