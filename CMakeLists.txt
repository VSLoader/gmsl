cmake_minimum_required(VERSION 3.8)

project("gmsl")

set(OutDir "${CMAKE_SOURCE_DIR}/out")

add_subdirectory("gmsl-loader")
add_subdirectory("gmsl-patcher")
add_subdirectory("gmsl-interop")