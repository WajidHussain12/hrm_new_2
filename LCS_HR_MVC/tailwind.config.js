/** @type {import('tailwindcss').Config} */
module.exports = {
  prefix: "tw-",
  content: [
    "./Views/**/*.cshtml",
    "./wwwroot/js/**/*.js"
  ],
  corePlugins: {
    preflight: false
  },
  theme: {
    extend: {
      colors: {
        brand: {
          50: "#f7f8ea",
          100: "#eef0cc",
          200: "#dde39f",
          300: "#c6cf68",
          400: "#aeb53f",
          500: "#8d9428",
          600: "#72791c",
          700: "#585d17",
          800: "#484b17",
          900: "#3f4217"
        },
        ink: {
          900: "#111827",
          800: "#1f2937",
          700: "#374151",
          600: "#4b5563"
        },
        mist: {
          50: "#f8fafc",
          100: "#f1f5f9",
          200: "#e2e8f0"
        }
      },
      boxShadow: {
        panel: "0 20px 45px -28px rgba(15, 23, 42, 0.35)",
        soft: "0 10px 25px -18px rgba(15, 23, 42, 0.35)"
      },
      fontFamily: {
        sans: ["Segoe UI", "Tahoma", "Geneva", "Verdana", "sans-serif"]
      }
    }
  },
  plugins: []
};
