cmake_minimum_required(VERSION 3.8)

set(CMAKE_CXX_STANDARD 17)

add_library(gmsl-interop MODULE
    src/interop.cpp
)

find_package(PkgConfig)

if(PkgConfig_FOUND)
    pkg_check_modules(MONO REQUIRED monosgen-2)
    target_include_directories(gmsl-interop PRIVATE ${MONO_INCLUDE_DIRS})
    target_link_directories(gmsl-interop PRIVATE ${MONO_LIBRARY_DIRS})
    string(REPLACE "monosgen-2.0" "mono-2.0-sgen" MONO_FIXED_LIBRARIES "${MONO_LIBRARIES}")
    target_link_libraries(gmsl-interop ${MONO_FIXED_LIBRARIES})
else()
    message(FATAL_ERROR "Cant find PkgConfig")
endif()

add_compile_definitions(OS_Windows)

set_target_properties(gmsl-interop PROPERTIES PREFIX "")

add_custom_command(TARGET gmsl-interop POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory "${OutDir}/gmsl/interop"
    COMMAND ${CMAKE_COMMAND} -E copy_directory "${MONO_LIBRARY_DIRS}/mono/4.5" "${OutDir}/gmsl/interop/lib/mono/4.5"
    COMMAND ${CMAKE_COMMAND} -E copy "${MONO_PREFIX}/bin/mono-2.0-sgen.dll" "${OutDir}/gmsl/interop"
    COMMAND ${CMAKE_COMMAND} -E copy $<TARGET_FILE:gmsl-interop> "${OutDir}/gmsl/interop"
)
