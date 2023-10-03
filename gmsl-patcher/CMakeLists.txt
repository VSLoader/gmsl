cmake_minimum_required(VERSION 3.8)

add_custom_target(gmsl-patcher ALL
	COMMAND dotnet build
	WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}
)

add_custom_command(TARGET gmsl-patcher POST_BUILD 
    COMMAND ${CMAKE_COMMAND} -E make_directory "${OutDir}/gmsl/patcher"
    COMMAND ${CMAKE_COMMAND} -E make_directory "${OutDir}/gmsl/mods"
    COMMAND ${CMAKE_COMMAND} -E copy_directory ${CMAKE_CURRENT_SOURCE_DIR}/bin/Debug/net6.0 "${OutDir}/gmsl/patcher"
)
