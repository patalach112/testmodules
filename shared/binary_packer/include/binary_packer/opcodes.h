#ifndef BINARY_PACKER_OPCODES_H
#define BINARY_PACKER_OPCODES_H

/*
 * Application-level opcodes carried in the BPP header. Shared by every
 * component so the gateway and its clients agree on message semantics.
 * Opcodes 0xF0..0xFF are reserved for protocol control messages.
 */

#define BP_OP_ECHO  0x01u /* payload is transformed and returned          */
#define BP_OP_ACK   0x02u /* acknowledgement, payload optional            */
#define BP_OP_ERROR 0xFFu /* protocol-level error, payload: status byte   */

/* Error payload values used with BP_OP_ERROR. */
#define BP_ERR_PAYLOAD_MALFORMED 0x01u

#endif /* BINARY_PACKER_OPCODES_H */
