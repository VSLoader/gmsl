cmake_minimum_required(VERSION 3.8)

set(CMAKE_CXX_STANDARD 17)

add_library(gmsl-loader SHARED 
    "src/dllmain.cpp" 
    "res/version.def"
)

set_target_properties(gmsl-loader PROPERTIES OUTPUT_NAME "version")

add_custom_command(TARGET gmsl-loader POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory "${OutDir}/gmsl/mods"
    COMMAND ${CMAKE_COMMAND} -E copy $<TARGET_FILE:gmsl-loader> ${OutDir}
)