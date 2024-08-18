cmake_minimum_required(VERSION 3.8)

set(CMAKE_CXX_STANDARD 17)

add_library(gmsl-interop MODULE
    src/interop.cpp
)

# find_package(PkgConfig)

# if(PkgConfig_FOUND)
#     pkg_check_modules(MONO REQUIRED monosgen-2)
#     target_include_directories(gmsl-interop PRIVATE ${MONO_INCLUDE_DIRS})
#     target_link_directories(gmsl-interop PRIVATE ${MONO_LIBRARY_DIRS})
#     string(REPLACE "monosgen-2.0" "mono-2.0-sgen" MONO_FIXED_LIBRARIES "${MONO_LIBRARIES}")
#     target_link_libraries(gmsl-interop ${MONO_FIXED_LIBRARIES})
# else()
#     message(FATAL_ERROR "Cant find PkgConfig")
# endif()

add_compile_definitions(OS_Windows)

set_target_properties(gmsl-interop PROPERTIES PREFIX "")

find_package(CoreClrEmbed)
if (CoreClrEmbed_FOUND)
    # target_compile_definitions(dotnet INTERFACE GMSL_DOTNET_RUNTIME_VERSION="${CoreClrEmbed_VERSION}")

    add_library(nethost SHARED IMPORTED)
    target_include_directories(nethost INTERFACE "${CoreClrEmbed_INCLUDE_DIRS}")
    get_filename_component(CoreClrEmbed_FOLDER ${CoreClrEmbed_SHARED_LIBRARIES} DIRECTORY)
    set_target_properties(nethost
            PROPERTIES
            IMPORTED_IMPLIB     ${CoreClrEmbed_SHARED_LIBRARIES}
            IMPORTED_LOCATION   ${CoreClrEmbed_LIBRARIES}
            BUILD_RPATH         ${CoreClrEmbed_FOLDER}
            INSTALL_RPATH       ${CoreClrEmbed_FOLDER})

    set(EXTRA_BUNDLE_LIBRARY_PATHS "${CoreClrEmbed_FOLDER}" PARENT_SCOPE)

    message(STATUS "Found .NET runtime!")

    target_link_directories(gmsl-interop INTERFACE ${CoreClrEmbed_FOLDER})
    target_include_directories(gmsl-interop PUBLIC ${CoreClrEmbed_INCLUDE_DIRS})
    # target_link_libraries(gmsl-interop ${CoreClrEmbed_LIBRARIES})
endif()



# TODO support linux
# <NetHostName Condition="$([MSBuild]::IsOsPlatform('Windows'))">nethost.dll</NetHostName>
# <NetHostName Condition="$([MSBuild]::IsOsPlatform('Linux'))">libnethost.so</NetHostName>
# <NetHostName Condition="$([MSBuild]::IsOsPlatform('OSX'))">libnethost.dylib</NetHostName>
# set(NET_HOST_DIR, "nethost.dll")
# set(NET_HOST_NAME, "nethost.dll")

# get_cmake_property(_variableNames VARIABLES)
# list (SORT _variableNames)
# foreach (_variableName ${_variableNames})
#     message(STATUS "${_variableName}=${${_variableName}}")
# endforeach()


add_custom_command(TARGET gmsl-interop POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory "${OutDir}/gmsl/interop"
    COMMAND ${CMAKE_COMMAND} -E copy ${CoreClrEmbed_SHARED_LIBRARIES} "${OutDir}/gmsl/interop/lib"
    # COMMAND ${CMAKE_COMMAND} -E copy_directory "${MONO_LIBRARY_DIRS}/mono/4.5" "${OutDir}/gmsl/interop/lib/mono/4.5"
    # COMMAND ${CMAKE_COMMAND} -E copy "${MONO_PREFIX}/bin/mono-2.0-sgen.dll" "${OutDir}/gmsl/interop"
    COMMAND ${CMAKE_COMMAND} -E copy $<TARGET_FILE:gmsl-interop> "${OutDir}/gmsl/interop"
)
