cmake_minimum_required(VERSION 3.15)
project(astcUtil)
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

add_definitions(-Wno-psabi -Wall -Wextra -fno-rtti -fno-exceptions -flto)
add_link_options(-Wl,--gc-sections -flto)

option(STATIC_LIBCPP "Use static libstdc++" OFF)
option(ISA_NEON "Set to ON to enable NEON instruction set" ${ODROIDN2})
if (ISA_NEON)
    set(ISA_NEON ON)
    set(ASTC_ENC_TARGET astcenc-neon-static)
else()
    set(ASTC_ENC_TARGET astcenc-native-static)
endif()

set(CLI OFF)
add_subdirectory(3rdparty/astc-encoder)

file(GLOB SOURCES *.cpp)

add_library(astcUtil SHARED ${SOURCES})
target_link_libraries(astcUtil PRIVATE pthread ${ASTC_ENC_TARGET})
target_include_directories(astcUtil PUBLIC 3rdparty/astc-encoder/Source)

if(STATIC_LIBCPP)
    target_link_options(${CMAKE_PROJECT_NAME} PRIVATE -static-libstdc++)
endif()
