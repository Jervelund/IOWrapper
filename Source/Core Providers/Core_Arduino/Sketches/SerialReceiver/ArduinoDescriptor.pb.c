/* Automatically generated nanopb constant definitions */
/* Generated by nanopb-0.3.9.2 at Mon Dec 17 23:17:15 2018. */

#include "ArduinoDescriptor.pb.h"

/* @@protoc_insertion_point(includes) */
#if PB_PROTO_HEADER_VERSION != 30
#error Regenerate this file with the current version of nanopb generator.
#endif



const pb_field_t arduino_ArduinoDescriptor_fields[4] = {
    PB_FIELD(  1, INT32   , SINGULAR, STATIC  , FIRST, arduino_ArduinoDescriptor, sequence, sequence, 0),
    PB_REPEATED_FIXED_COUNT(  2, INT32   , OTHER, arduino_ArduinoDescriptor, axis, sequence, 0),
    PB_REPEATED_FIXED_COUNT(  3, BOOL    , OTHER, arduino_ArduinoDescriptor, button, axis, 0),
    PB_LAST_FIELD
};


/* @@protoc_insertion_point(eof) */
