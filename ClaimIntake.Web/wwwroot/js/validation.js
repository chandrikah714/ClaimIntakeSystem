// Client-side validation helpers for the Claim form

// Auto-uppercase the ICD-10 field as the user types
document.addEventListener('DOMContentLoaded', function () {

    var diagField = document.getElementById('DiagnosisCode')
        || document.getElementById('diagnosisInput');

    if (diagField) {
        diagField.addEventListener('blur', function () {
            this.value = this.value.toUpperCase().trim();
        });

        diagField.addEventListener('input', function () {
            var val = this.value.toUpperCase().trim();
            var preview = document.getElementById('icdPreview');
            var pattern = /^[A-Z]\d{2}(\.\w{1,4})?$/;

            if (preview) {
                if (val.length >= 3) {
                    preview.style.display = 'block';
                    if (pattern.test(val)) {
                        preview.className = 'icd-preview icd-valid';
                        preview.textContent = 'Valid ICD-10 format: ' + val;
                    } else {
                        preview.className = 'icd-preview icd-invalid';
                        preview.textContent = 'Invalid format. Example: A01.1';
                    }
                } else {
                    preview.style.display = 'none';
                }
            }
        });
    }

    // Prevent double-submit on the claim form
    var form = document.getElementById('claimForm');
    if (form) {
        form.addEventListener('submit', function () {
            var btn = document.getElementById('submitBtn');
            if (btn) {
                btn.disabled = true;
                btn.textContent = 'Submitting...';
            }
        });
    }
});