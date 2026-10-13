use adblock::{request::Request, Engine};
use std::{
    ffi::CStr,
    os::raw::c_char,
    panic::{catch_unwind, AssertUnwindSafe},
    ptr,
};

pub struct UrlScraAdblock {
    engine: Engine,
}

fn read_utf8(value: *const c_char) -> Option<String> {
    if value.is_null() {
        return None;
    }

    unsafe { CStr::from_ptr(value) }
        .to_str()
        .ok()
        .map(str::to_owned)
}

#[no_mangle]
pub extern "C" fn urlscra_adblock_create(rules: *const c_char) -> *mut UrlScraAdblock {
    catch_unwind(AssertUnwindSafe(|| {
        let rules = match read_utf8(rules) {
            Some(value) if !value.trim().is_empty() => value,
            _ => return ptr::null_mut(),
        };

        let engine = Engine::new_with_list_text(rules);
        Box::into_raw(Box::new(UrlScraAdblock { engine }))
    }))
    .unwrap_or(ptr::null_mut())
}

#[no_mangle]
pub extern "C" fn urlscra_adblock_destroy(handle: *mut UrlScraAdblock) {
    if handle.is_null() {
        return;
    }

    let _ = catch_unwind(AssertUnwindSafe(|| unsafe {
        drop(Box::from_raw(handle));
    }));
}

#[no_mangle]
pub extern "C" fn urlscra_adblock_should_block(
    handle: *const UrlScraAdblock,
    url: *const c_char,
    source_url: *const c_char,
    resource_type: *const c_char,
    method: *const c_char,
) -> i32 {
    catch_unwind(AssertUnwindSafe(|| {
        if handle.is_null() {
            return 0;
        }

        let url = match read_utf8(url) {
            Some(value) => value,
            None => return 0,
        };
        let source_url = read_utf8(source_url).unwrap_or_else(|| url.clone());
        let resource_type = read_utf8(resource_type).unwrap_or_else(|| "other".to_string());
        let method = read_utf8(method).unwrap_or_else(|| "get".to_string());

        // Always allow the top-level document. URLScra uses adblock-rust to reduce
        // third-party noise, not to prevent the user-requested page from loading.
        if resource_type.eq_ignore_ascii_case("document") ||
            resource_type.eq_ignore_ascii_case("main_frame") {
            return 0;
        }

        let request = match Request::new(&url, &source_url, &resource_type, &method) {
            Ok(request) => request,
            Err(_) => return 0,
        };

        let blocker_result = unsafe { &*handle }.engine.check_network_request(&request);
        if blocker_result.should_block() { 1 } else { 0 }
    }))
    .unwrap_or(0)
}
