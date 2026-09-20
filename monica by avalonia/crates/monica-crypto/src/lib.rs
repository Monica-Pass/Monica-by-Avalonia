// Unsafe is confined to the FFI boundary: raw pointers from C# are converted to
// slices here, then all downstream work goes through safe argon2 crate APIs.

use argon2::{Algorithm, Argon2, Params, Version};
use std::os::raw::c_uint;

pub const OUTPUT_LEN: usize = 32;

#[repr(C)]
pub enum KdfError {
    Success = 0,
    NullPointer = 1,
    ZeroIterations = 2,
    ZeroParallelism = 3,
    InvalidParams = 4,
    DerivationFailed = 5,
}

#[no_mangle]
pub extern "C" fn monica_argon2id(
    password_ptr: *const u8,
    password_len: c_uint,
    salt_ptr: *const u8,
    salt_len: c_uint,
    iterations: c_uint,
    memory_kib: c_uint,
    parallelism: c_uint,
    output_ptr: *mut u8,
) -> KdfError {
    if password_ptr.is_null() || salt_ptr.is_null() || output_ptr.is_null() {
        return KdfError::NullPointer;
    }
    if iterations == 0 {
        return KdfError::ZeroIterations;
    }
    if parallelism == 0 {
        return KdfError::ZeroParallelism;
    }

    let password = unsafe { std::slice::from_raw_parts(password_ptr, password_len as usize) };
    let salt = unsafe { std::slice::from_raw_parts(salt_ptr, salt_len as usize) };
    let output = unsafe { std::slice::from_raw_parts_mut(output_ptr, OUTPUT_LEN) };

    let params = match Params::new(memory_kib, iterations, parallelism, Some(OUTPUT_LEN)) {
        Ok(p) => p,
        Err(_) => return KdfError::InvalidParams,
    };

    let argon2 = Argon2::new(Algorithm::Argon2id, Version::V0x13, params);
    match argon2.hash_password_into(password, salt, output) {
        Ok(()) => KdfError::Success,
        Err(_) => KdfError::DerivationFailed,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn decode_hex(value: &str) -> Vec<u8> {
        assert_eq!(value.len() % 2, 0);
        value
            .as_bytes()
            .chunks_exact(2)
            .map(|pair| {
                let pair = std::str::from_utf8(pair).expect("test vector must be ASCII");
                u8::from_str_radix(pair, 16).expect("test vector must be valid hex")
            })
            .collect()
    }

    #[test]
    fn argon2id_v13_matches_known_vector() {
        let password = b"password";
        let salt = b"somesalt";
        let mut output = [0_u8; OUTPUT_LEN];

        let result = monica_argon2id(
            password.as_ptr(),
            password.len() as c_uint,
            salt.as_ptr(),
            salt.len() as c_uint,
            2,
            32,
            1,
            output.as_mut_ptr(),
        );

        assert!(matches!(result, KdfError::Success));
        let expected =
            decode_hex("31111cc053ba0a799c0884148fd7ec9dc3631f3e8cf476cca9521d4ccc5136e8");
        assert_eq!(output.as_slice(), expected.as_slice());
    }

    #[test]
    fn argon2id_rejects_zero_iterations() {
        let password = b"password";
        let salt = b"somesalt";
        let mut output = [0_u8; OUTPUT_LEN];

        let result = monica_argon2id(
            password.as_ptr(),
            password.len() as c_uint,
            salt.as_ptr(),
            salt.len() as c_uint,
            0,
            32,
            1,
            output.as_mut_ptr(),
        );

        assert!(matches!(result, KdfError::ZeroIterations));
    }

    #[test]
    fn argon2id_rejects_null_pointers() {
        let mut output = [0_u8; OUTPUT_LEN];

        let result = monica_argon2id(
            std::ptr::null(),
            0,
            std::ptr::null(),
            0,
            2,
            32,
            1,
            output.as_mut_ptr(),
        );

        assert!(matches!(result, KdfError::NullPointer));
    }
}
