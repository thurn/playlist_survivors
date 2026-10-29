//! Long local analysis/simulation jobs run below the utility-class slow checks.
pub fn enter() -> Result<(), String> {
    #[cfg(target_os = "macos")]
    if std::env::var_os("PS_NATIVE_BATCH").is_none() {
        use std::os::unix::process::CommandExt;
        let error = std::process::Command::new("taskpolicy")
            .arg("-b")
            .arg(std::env::current_exe().map_err(|e| e.to_string())?)
            .args(std::env::args_os().skip(1))
            .env("PS_NATIVE_BATCH", "1")
            .exec();
        return Err(format!("taskpolicy -b: {error}"));
    }
    Ok(())
}
