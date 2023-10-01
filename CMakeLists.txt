cmake_minimum_required(VERSION 3.8)

project("gmsl")

include(FetchContent)
FetchContent_Declare(
    undertalemodtool
    GIT_REPOSITORY https://github.com/krzys-h/UndertaleModTool.git
    GIT_TAG 7b876ad457eca5cdd69957dc02ef57a569412e5e
)
FetchContent_MakeAvailable(undertalemodtool)

file(COPY ${undertalemodtool_SOURCE_DIR} DESTINATION "${CMAKE_SOURCE_DIR}/vendor")

set(OutDir "${CMAKE_SOURCE_DIR}/out")

add_subdirectory("gmsl-loader")
add_subdirectory("gmsl-patcher")